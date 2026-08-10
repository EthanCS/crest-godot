using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# animated-waves manager: shape wave buffer, local bumps and final combine.
[GlobalClass]
public partial class CrestAnimatedWavesManagerCs : RefCounted
{
    public CrestLodDataMgrCs Data { get; } = new();
    public Texture2DArrayRD texture_array => Data.TextureArray;
    public Rid wave_buffer { get; private set; }
    private RenderingDevice? _device;
    private CrestRDComputeCs? _combine;
    private CrestRDComputeCs? _clear;
    private CrestRDComputeCs? _inject;
    private Rid _fallbackDynamicWaves;

    public void init_mgr(int resolution, int layers)
    {
        Data.InitSim(resolution, layers, RenderingDevice.DataFormat.R16G16B16A16Sfloat, false);
        _device = Data.Device;
        if (_device == null) return;
        var format = new RDTextureFormat
        {
            Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
            Width = (uint)resolution, Height = (uint)resolution, ArrayLayers = (uint)layers,
            TextureType = RenderingDevice.TextureType.Type2DArray,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit,
        };
        wave_buffer = _device.TextureCreate(format, new RDTextureView());
        _fallbackDynamicWaves = MakeFallbackDynamicWaves();
        _combine = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/shape_combine.glsl");
        _clear = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/clear.glsl");
        _inject = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/inject_anim_waves.glsl");
    }

    public Variant make_sampled_uniform(int binding) => Data.MakeSampledUniform((uint)binding);
    public Variant current_texture() => Data.CurrentTexture();

    public void update(Array shapes, GodotObject? depthManager, GodotObject? dynamicWavesManager,
        GodotObject? lodTransform, Rid cascadeBuffer, double oceanScale, double oceanLevel,
        double time, Resource? dynamicSettings, Array inputs)
    {
        if (_device == null || !wave_buffer.IsValid || _combine == null || !_combine.IsValid) return;
        if (shapes.Count == 0)
            ClearWaveBuffer();
        else
        {
            var first = true;
            foreach (var value in shapes)
            {
                if (value.VariantType != Variant.Type.Object || value.AsGodotObject() is not GodotObject shape) continue;
#pragma warning disable CS8604
                shape.Call("evaluate", wave_buffer, depthManager, lodTransform, oceanScale, oceanLevel, time, !first);
#pragma warning restore CS8604
                first = false;
            }
        }
        foreach (var value in inputs)
            if (value.VariantType == Variant.Type.Dictionary)
                DispatchInput(cascadeBuffer, value.AsGodotDictionary());
        DispatchCombine(cascadeBuffer, dynamicWavesManager, dynamicSettings);
    }

    public void free_rids()
    {
        _combine?.DisposeRid(); _clear?.DisposeRid(); _inject?.DisposeRid();
        if (_device != null)
        {
            if (wave_buffer.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, wave_buffer);
            if (_fallbackDynamicWaves.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, _fallbackDynamicWaves);
        }
        wave_buffer = new Rid(); _fallbackDynamicWaves = new Rid(); Data.FreeRids();
    }

    private void ClearWaveBuffer()
    {
        if (_clear == null || !_clear.IsValid) return;
        var image = ImageUniform(0, wave_buffer);
        var set = _clear.MakeUniformSet(new Array<RDUniform> { image });
        _clear.Dispatch(Groups(), Groups(), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set },
            CrestRDComputeCs.PackPushConstants(new[] { (float)Data.Resolution, 0.0f, 0.0f, 0.0f, 0.0f }));
        CrestRDComputeCs.FreeUniformSetDeferred(_device!, set);
    }

    private void DispatchInput(Rid cascadeBuffer, global::Godot.Collections.Dictionary input)
    {
        if (_inject == null || !_inject.IsValid) return;
        var set = _inject.MakeUniformSet(new Array<RDUniform>
        {
            StorageUniform(0, cascadeBuffer), ImageUniform(1, wave_buffer),
        });
        var center = input.ContainsKey("rect_center") ? (Vector2)input["rect_center"] : Vector2.Zero;
        var values = new[] { (float)Data.Resolution, (float)Data.LayerCount, center.X, center.Y,
            input.ContainsKey("radius") ? (float)input["radius"] : 3.0f,
            input.ContainsKey("amplitude") ? (float)input["amplitude"] : 1.0f,
            input.ContainsKey("blend_mode") ? (float)input["blend_mode"] : 0.0f, 0.1f };
        _inject.Dispatch(Groups(), Groups(), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set }, CrestRDComputeCs.PackPushConstants(values));
        CrestRDComputeCs.FreeUniformSetDeferred(_device!, set);
    }

    private void DispatchCombine(Rid cascadeBuffer, GodotObject? dynamicManager, Resource? settings)
    {
        var dynamicTexture = _fallbackDynamicWaves;
        var dynamicEnabled = dynamicManager != null && dynamicManager.HasMethod("current_texture");
        if (dynamicEnabled)
        {
            var value = dynamicManager!.Call("current_texture");
            if (value.VariantType == Variant.Type.Rid) dynamicTexture = value.AsRid();
        }
        var wave = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 };
        wave.AddId(Data.Sampler); wave.AddId(wave_buffer);
        var dyn = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 3 };
        dyn.AddId(Data.Sampler); dyn.AddId(dynamicTexture);
        var set = _combine!.MakeUniformSet(new Array<RDUniform>
        {
            StorageUniform(0, cascadeBuffer), wave, Data.MakeImageUniform(2, false), dyn,
        });
        var horizontal = settings != null ? (float)settings.Get("horiz_displace") : 3.0f;
        var clamp = settings != null ? (float)settings.Get("displace_clamp") : 0.3f;
        var values = new[] { (float)Data.Resolution, (float)Data.LayerCount, horizontal, clamp, dynamicEnabled ? 1.0f : 0.0f };
        _combine.Dispatch(Groups(), Groups(), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set }, CrestRDComputeCs.PackPushConstants(values));
        CrestRDComputeCs.FreeUniformSetDeferred(_device!, set);
    }

    private Rid MakeFallbackDynamicWaves()
    {
        var format = new RDTextureFormat { Format = RenderingDevice.DataFormat.R16G16Sfloat,
            Width = 1, Height = 1, ArrayLayers = 2, TextureType = RenderingDevice.TextureType.Type2DArray,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit };
        return _device!.TextureCreate(format, new RDTextureView(), new Array<byte[]> { new byte[4], new byte[4] });
    }
    private uint Groups() => (uint)Mathf.CeilToInt(Data.Resolution / 8.0f);
    private static RDUniform StorageUniform(uint binding, Rid rid)
    {
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = (int)binding };
        uniform.AddId(rid); return uniform;
    }
    private static RDUniform ImageUniform(uint binding, Rid rid)
    {
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = (int)binding };
        uniform.AddId(rid); return uniform;
    }
}
