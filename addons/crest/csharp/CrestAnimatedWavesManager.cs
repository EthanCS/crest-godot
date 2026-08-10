using Godot;
using Godot.Collections;
using System.Collections.Generic;

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
    private readonly System.Collections.Generic.Dictionary<ulong, Rid> _inputTextureCache = new();
    private Rid _fallbackInputTexture;

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

    public Rid GetCurrentTexture() => Data.CurrentTexture();

    public void update(IReadOnlyList<ICrestShapeGenerator> shapes,
        CrestSeaFloorDepthManagerCs? depthManager, CrestDynamicWavesManagerCs? dynamicWavesManager,
        CrestLodTransformCs lodTransform, Rid cascadeBuffer, double oceanScale, double oceanLevel,
        double time, CrestSimSettingsWave? dynamicSettings, Array inputs)
    {
        if (_device == null || !wave_buffer.IsValid || _combine == null || !_combine.IsValid) return;
        if (shapes.Count == 0)
            ClearWaveBuffer();
        else
        {
            var first = true;
            foreach (var shape in shapes)
            {
                shape.Evaluate(wave_buffer, depthManager, lodTransform, oceanScale, oceanLevel, time, !first);
                first = false;
            }
        }
        foreach (var value in inputs)
            if (value.VariantType == Variant.Type.Dictionary && InputWavelength(value.AsGodotDictionary()) != 0.0f)
                DispatchInput(cascadeBuffer, value.AsGodotDictionary(), (float)oceanLevel, wave_buffer);
        DispatchCombine(cascadeBuffer, dynamicWavesManager, dynamicSettings);
        foreach (var value in inputs)
            if (value.VariantType == Variant.Type.Dictionary && InputWavelength(value.AsGodotDictionary()) == 0.0f)
                DispatchInput(cascadeBuffer, value.AsGodotDictionary(), (float)oceanLevel, Data.CurrentTexture());
    }

    public void free_rids()
    {
        _combine?.DisposeRid(); _clear?.DisposeRid(); _inject?.DisposeRid();
        if (_device != null)
        {
            if (wave_buffer.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, wave_buffer);
            if (_fallbackDynamicWaves.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, _fallbackDynamicWaves);
            foreach (var rid in _inputTextureCache.Values)
                if (rid.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, rid);
            if (_fallbackInputTexture.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, _fallbackInputTexture);
        }
        _inputTextureCache.Clear();
        wave_buffer = new Rid(); _fallbackDynamicWaves = new Rid(); _fallbackInputTexture = new Rid(); Data.FreeRids();
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

    private void DispatchInput(Rid cascadeBuffer, global::Godot.Collections.Dictionary input, float oceanLevel, Rid target)
    {
        if (_inject == null || !_inject.IsValid) return;
        var textureUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 2 };
        textureUniform.AddId(Data.Sampler); textureUniform.AddId(InputTextureRid(GetInputTexture(input)));
        var set = _inject.MakeUniformSet(new Array<RDUniform>
        {
            StorageUniform(0, cascadeBuffer), ImageUniform(1, target), textureUniform,
        });
        var center = input.ContainsKey("rect_center") ? (Vector2)input["rect_center"] : Vector2.Zero;
        var half = input.ContainsKey("rect_half_size") ? (Vector2)input["rect_half_size"] : Vector2.One;
        var values = new[] { (float)Data.Resolution, (float)Data.LayerCount, center.X, center.Y, half.X, half.Y,
            input.ContainsKey("amplitude") ? (float)input["amplitude"] : 1.0f,
            input.ContainsKey("blend_mode") ? (float)input["blend_mode"] : 0.0f,
            input.ContainsKey("heights_only") ? (float)input["heights_only"] : 1.0f, oceanLevel,
            InputWavelength(input) };
        _inject.Dispatch(Groups(), Groups(), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set }, CrestRDComputeCs.PackPushConstants(values));
        CrestRDComputeCs.FreeUniformSetDeferred(_device!, set);
    }

    private static float InputWavelength(global::Godot.Collections.Dictionary input) =>
        input.ContainsKey("wavelength") ? (float)input["wavelength"] : 0.0f;

    private static Texture2D? GetInputTexture(global::Godot.Collections.Dictionary input)
    {
        if (!input.ContainsKey("texture")) return null;
        var value = input["texture"];
        return value.VariantType == Variant.Type.Object ? value.AsGodotObject() as Texture2D : null;
    }

    private Rid InputTextureRid(Texture2D? texture)
    {
        if (_device == null || texture == null) return FallbackInputTexture();
        var key = texture.GetInstanceId();
        if (_inputTextureCache.TryGetValue(key, out var cached)) return cached;
        var image = texture.GetImage();
        if (image == null || image.IsEmpty()) return FallbackInputTexture();
        image.Convert(Image.Format.Rgbaf);
        var format = new RDTextureFormat { Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat,
            Width = (uint)image.GetWidth(), Height = (uint)image.GetHeight(),
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit };
        var rid = _device.TextureCreate(format, new RDTextureView(), new Array<byte[]> { image.GetData() });
        _inputTextureCache[key] = rid; return rid;
    }

    private Rid FallbackInputTexture()
    {
        if (_device == null || _fallbackInputTexture.IsValid) return _fallbackInputTexture;
        var format = new RDTextureFormat { Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat,
            Width = 1, Height = 1, UsageBits = RenderingDevice.TextureUsageBits.SamplingBit |
                RenderingDevice.TextureUsageBits.CanUpdateBit };
        _fallbackInputTexture = _device.TextureCreate(format, new RDTextureView(),
            new Array<byte[]> { new byte[16] });
        return _fallbackInputTexture;
    }

    private void DispatchCombine(Rid cascadeBuffer, CrestDynamicWavesManagerCs? dynamicManager,
        CrestSimSettingsWave? settings)
    {
        var dynamicTexture = _fallbackDynamicWaves;
        var dynamicEnabled = dynamicManager != null;
        if (dynamicManager != null) dynamicTexture = dynamicManager.Data.CurrentTexture();
        var wave = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 };
        wave.AddId(Data.Sampler); wave.AddId(wave_buffer);
        var dyn = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 3 };
        dyn.AddId(Data.Sampler); dyn.AddId(dynamicTexture);
        var set = _combine!.MakeUniformSet(new Array<RDUniform>
        {
            StorageUniform(0, cascadeBuffer), wave, Data.MakeImageUniform(2, false), dyn,
        });
        var horizontal = settings?._horizDisplace ?? 3.0f;
        var clamp = settings?._displaceClamp ?? 0.3f;
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
