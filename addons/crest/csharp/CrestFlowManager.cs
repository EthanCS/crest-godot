using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# flow manager: clears the RG16F cascade and injects flow-map or fixed
/// velocity inputs using the production compute shaders.
[GlobalClass]
public partial class CrestFlowManagerCs : RefCounted
{
    public CrestLodDataMgrCs Data { get; } = new();
    public Texture2DArrayRD texture_array => Data.TextureArray;
    private RenderingDevice? _device;
    private CrestRDComputeCs? _clear;
    private CrestRDComputeCs? _inject;
    private readonly System.Collections.Generic.Dictionary<ulong, Rid> _textureCache = new();
    private Rid _fallbackTexture;

    public void init_mgr(int resolution, int layers, Resource? settings = null)
    {
        Data.InitSim(resolution, layers, RenderingDevice.DataFormat.R16G16Sfloat, false);
        _device = Data.Device;
        if (_device != null)
        {
            _clear = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/clear_rg16f.glsl");
            _inject = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/inject_flow.glsl");
        }
    }

    public Variant make_sampled_uniform(int binding) => Data.MakeSampledUniform((uint)binding);

    public void update_sim(GodotObject? lodTransform, Rid cascadeCurrent, Array inputs)
    {
        if (_device == null || _clear == null || !_clear.IsValid) return;
        var clearSet = _clear.MakeUniformSet(new Array<RDUniform> { Data.MakeImageUniform(0, false) });
        _clear.Dispatch(Groups(), Groups(), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = clearSet },
            CrestRDComputeCs.PackPushConstants(new[] { (float)Data.Resolution, 0.0f, 0.0f }));
        CrestRDComputeCs.FreeUniformSetDeferred(_device, clearSet);
        if (_inject == null || !_inject.IsValid) return;
        foreach (var value in inputs)
            if (value.VariantType == Variant.Type.Dictionary)
                DispatchInput(cascadeCurrent, value.AsGodotDictionary());
    }

    public void free_rids()
    {
        _clear?.DisposeRid();
        _inject?.DisposeRid();
        if (_device != null)
        {
            foreach (var rid in _textureCache.Values)
                if (rid.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, rid);
            if (_fallbackTexture.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, _fallbackTexture);
        }
        _textureCache.Clear();
        _fallbackTexture = new Rid();
        Data.FreeRids();
    }

    private void DispatchInput(Rid cascadeCurrent, global::Godot.Collections.Dictionary input)
    {
        var texture = GetTexture(input);
        var texUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 };
        texUniform.AddId(Data.Sampler);
        texUniform.AddId(TextureRid(texture));
        var set = _inject!.MakeUniformSet(new Array<RDUniform>
        {
            StorageUniform(0, cascadeCurrent), texUniform, Data.MakeImageUniform(2, false),
        });
        var center = input.ContainsKey("rect_center") ? (Vector2)input["rect_center"] : Vector2.Zero;
        var half = input.ContainsKey("rect_half_size") ? (Vector2)input["rect_half_size"] : Vector2.One;
        var velocity = input.ContainsKey("fixed_velocity") ? (Vector2)input["fixed_velocity"] : Vector2.Zero;
        var values = new[]
        {
            (float)Data.Resolution, (float)Data.LayerCount, center.X, center.Y, half.X, half.Y,
            velocity.X, velocity.Y,
            input.ContainsKey("strength") ? (float)input["strength"] : 1.0f,
            input.ContainsKey("mode") ? (float)input["mode"] : 0.0f,
            texture != null ? 1.0f : 0.0f,
        };
        _inject.Dispatch(Groups(), Groups(), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set },
            CrestRDComputeCs.PackPushConstants(values));
        CrestRDComputeCs.FreeUniformSetDeferred(_device!, set);
    }

    private uint Groups() => (uint)Mathf.CeilToInt(Data.Resolution / 8.0f);
    private static RDUniform StorageUniform(uint binding, Rid rid)
    {
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = (int)binding };
        uniform.AddId(rid);
        return uniform;
    }
    private static Texture2D? GetTexture(global::Godot.Collections.Dictionary input)
    {
        if (!input.ContainsKey("texture")) return null;
        var value = input["texture"];
        return value.VariantType == Variant.Type.Object ? value.AsGodotObject() as Texture2D : null;
    }
    private Rid TextureRid(Texture2D? texture)
    {
        if (_device == null || texture == null) return FallbackTexture();
        var key = texture.GetInstanceId();
        if (_textureCache.TryGetValue(key, out var cached)) return cached;
        var image = texture.GetImage();
        if (image == null || image.IsEmpty()) return FallbackTexture();
        image.Convert(Image.Format.Rgbaf);
        var format = new RDTextureFormat { Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat,
            Width = (uint)image.GetWidth(), Height = (uint)image.GetHeight(),
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit };
        var rid = _device.TextureCreate(format, new RDTextureView(), new Array<byte[]> { image.GetData() });
        _textureCache[key] = rid;
        return rid;
    }
    private Rid FallbackTexture()
    {
        if (_device == null || _fallbackTexture.IsValid) return _fallbackTexture;
        var format = new RDTextureFormat { Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat, Width = 1, Height = 1,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit };
        var values = new[] { 0.5f, 0.5f, 0.0f, 0.0f };
        _fallbackTexture = _device.TextureCreate(format, new RDTextureView(),
            new Array<byte[]> { CrestRDComputeCs.PackPushConstants(values) });
        return _fallbackTexture;
    }
}
