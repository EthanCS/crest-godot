using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# R8 clip-surface manager with default clear and masked clip/unclip inputs.
[GlobalClass]
public partial class CrestClipSurfaceManagerCs : RefCounted
{
    public CrestLodDataMgrCs Data { get; } = new();
    public Texture2DArrayRD texture_array => Data.TextureArray;
    public bool ClipByDefault { get; private set; } = true;
    private RenderingDevice? _device;
    private CrestRDComputeCs? _clear;
    private CrestRDComputeCs? _inject;
    private readonly System.Collections.Generic.Dictionary<ulong, Rid> _textureCache = new();
    private Rid _fallbackTexture;

    public void init_mgr(int resolution, int layers, Resource? settings = null)
    {
        if (settings != null && settings.Get("clip_by_default").VariantType == Variant.Type.Bool)
            ClipByDefault = (bool)settings.Get("clip_by_default");
        Data.InitSim(resolution, layers, RenderingDevice.DataFormat.R8Unorm, false);
        _device = Data.Device;
        if (_device != null)
        {
            _clear = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/clear_r8.glsl");
            _inject = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/inject_clip.glsl");
        }
    }

    public Variant make_sampled_uniform(int binding) => Data.MakeSampledUniform((uint)binding);

    public void update_sim(GodotObject? lodTransform, Rid cascadeCurrent, Array inputs)
    {
        if (_device == null || _clear == null || !_clear.IsValid) return;
        var clearSet = _clear.MakeUniformSet(new Array<RDUniform> { Data.MakeImageUniform(0, false) });
        _clear.Dispatch(Groups(), Groups(), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = clearSet },
            CrestRDComputeCs.PackPushConstants(new[] { (float)Data.Resolution, ClipByDefault ? 1.0f : 0.0f }));
        CrestRDComputeCs.FreeUniformSetDeferred(_device, clearSet);
        if (_inject == null || !_inject.IsValid) return;
        foreach (var value in inputs)
            if (value.VariantType == Variant.Type.Dictionary)
                DispatchInput(cascadeCurrent, value.AsGodotDictionary());
    }

    public void free_rids()
    {
        _clear?.DisposeRid(); _inject?.DisposeRid();
        if (_device != null)
        {
            foreach (var rid in _textureCache.Values)
                if (rid.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, rid);
            if (_fallbackTexture.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, _fallbackTexture);
        }
        _textureCache.Clear(); _fallbackTexture = new Rid(); Data.FreeRids();
    }

    private void DispatchInput(Rid cascadeCurrent, global::Godot.Collections.Dictionary input)
    {
        var texture = GetTexture(input);
        var texUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 };
        texUniform.AddId(Data.Sampler); texUniform.AddId(TextureRid(texture));
        var set = _inject!.MakeUniformSet(new Array<RDUniform>
        {
            StorageUniform(cascadeCurrent), texUniform, Data.MakeImageUniform(2, false),
        });
        var center = input.ContainsKey("rect_center") ? (Vector2)input["rect_center"] : Vector2.Zero;
        var half = input.ContainsKey("rect_half_size") ? (Vector2)input["rect_half_size"] : Vector2.One;
        var values = new[] { (float)Data.Resolution, (float)Data.LayerCount, center.X, center.Y, half.X, half.Y,
            input.ContainsKey("mode") ? (float)input["mode"] : 0.0f, texture != null ? 1.0f : 0.0f };
        _inject.Dispatch(Groups(), Groups(), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set }, CrestRDComputeCs.PackPushConstants(values));
        CrestRDComputeCs.FreeUniformSetDeferred(_device!, set);
    }

    private uint Groups() => (uint)Mathf.CeilToInt(Data.Resolution / 8.0f);
    private static RDUniform StorageUniform(Rid rid)
    {
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 0 };
        uniform.AddId(rid); return uniform;
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
        var format = TextureFormat((uint)image.GetWidth(), (uint)image.GetHeight());
        var rid = _device.TextureCreate(format, new RDTextureView(), new Array<byte[]> { image.GetData() });
        _textureCache[key] = rid; return rid;
    }
    private Rid FallbackTexture()
    {
        if (_device == null || _fallbackTexture.IsValid) return _fallbackTexture;
        _fallbackTexture = _device.TextureCreate(TextureFormat(1, 1), new RDTextureView(),
            new Array<byte[]> { CrestRDComputeCs.PackPushConstants(new[] { 1.0f, 1.0f, 1.0f, 1.0f }) });
        return _fallbackTexture;
    }
    private static RDTextureFormat TextureFormat(uint width, uint height) => new()
    {
        Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat, Width = width, Height = height,
        UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit,
    };
}
