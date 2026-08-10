using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# RG8 soft/hard shadow manager using analytic sphere casters and EMA.
[GlobalClass]
public partial class CrestShadowManagerCs : RefCounted
{
    private const int MaxCasters = 128;
    public CrestLodDataMgrCs Data { get; } = new();
    public Texture2DArrayRD texture_array => Data.TextureArray;
    public Vector3 light_dir { get; set; } = Vector3.Up;
    public CrestSimSettingsShadow Settings { get; private set; } = new();
    private RenderingDevice? _device;
    private CrestRDComputeCs? _update;
    private CrestRDComputeCs? _injectOverride;
    private Rid _casterBuffer;
    private readonly System.Collections.Generic.Dictionary<ulong, Rid> _textureCache = new();
    private Rid _fallbackTexture;

    public void init_mgr(int resolution, int layers, Resource? source = null)
    {
        Settings = source as CrestSimSettingsShadow ?? new CrestSimSettingsShadow();
        Data.InitSim(resolution, layers, RenderingDevice.DataFormat.R8G8Unorm, true, new Color(1, 1, 0, 0));
        _device = Data.Device;
        if (_device != null)
        {
            _update = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/update_shadow.glsl");
            _injectOverride = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/inject_shadow_override.glsl");
            _casterBuffer = _device.StorageBufferCreate(MaxCasters * 16u);
        }
    }

    public void update_sim(double delta, CrestLodTransformCs lodTransform, Rid cascadeCurrent,
        Rid cascadeSource, CrestAnimatedWavesManagerCs? animatedWaves, double oceanLevel,
        double lodChange, double time, Array casters)
    {
        _ = lodTransform;
        if (_device == null || _update == null || !_update.IsValid) return;
        var values = new float[Mathf.Min(casters.Count, MaxCasters) * 4];
        var count = 0;
        foreach (var value in casters)
        {
            if (count >= MaxCasters || value.VariantType != Variant.Type.Dictionary) break;
            var caster = value.AsGodotDictionary();
            if (!caster.ContainsKey("pos")) continue;
            var position = (Vector3)caster["pos"];
            var offset = count * 4;
            values[offset] = position.X; values[offset + 1] = position.Y; values[offset + 2] = position.Z;
            values[offset + 3] = caster.ContainsKey("radius") ? (float)caster["radius"] : 1.0f;
            count++;
        }
        if (count > 0)
            _device.BufferUpdate(_casterBuffer, 0, (uint)(count * 16), FloatsToBytes(values, count * 4));
        var uniforms = new Array<RDUniform>
        {
            StorageUniform(0, cascadeCurrent), StorageUniform(1, cascadeSource),
            Data.MakeSampledUniform(2), Data.MakeImageUniform(3), StorageUniform(4, _casterBuffer),
            animatedWaves?.Data.MakeSampledUniform(5) ?? Data.MakeSampledUniform(5),
        };
        var set = _update.MakeUniformSet(uniforms);
        var push = new[] { (float)Data.Resolution, (float)Data.LayerCount, (float)delta, (float)lodChange,
            (float)count, Settings._jitterDiameterSoft, Settings._currentFrameWeightSoft,
            Settings._jitterDiameterHard, Settings._currentFrameWeightHard,
            light_dir.X, light_dir.Y, light_dir.Z, (float)time, (float)oceanLevel, 1.0f };
        _update.Dispatch(Groups(), Groups(), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set }, CrestRDComputeCs.PackPushConstants(push));
        CrestRDComputeCs.FreeUniformSetDeferred(_device, set);
        Data.SwapTargets();
        if (_injectOverride != null && _injectOverride.IsValid)
            foreach (var value in casters)
                if (value.VariantType == Variant.Type.Dictionary && value.AsGodotDictionary().ContainsKey("rect_center"))
                    DispatchOverride(cascadeCurrent, value.AsGodotDictionary());
    }

    public void free_rids()
    {
        _update?.DisposeRid(); _injectOverride?.DisposeRid();
        if (_device != null)
        {
            if (_casterBuffer.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, _casterBuffer);
            foreach (var rid in _textureCache.Values)
                if (rid.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, rid);
            if (_fallbackTexture.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, _fallbackTexture);
        }
        _textureCache.Clear(); _casterBuffer = new Rid(); _fallbackTexture = new Rid(); Data.FreeRids();
    }

    private void DispatchOverride(Rid cascadeCurrent, global::Godot.Collections.Dictionary input)
    {
        var texture = GetTexture(input);
        var sampled = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 };
        sampled.AddId(Data.Sampler); sampled.AddId(TextureRid(texture));
        var set = _injectOverride!.MakeUniformSet(new Array<RDUniform>
        {
            StorageUniform(0, cascadeCurrent), sampled, Data.MakeImageUniform(2, false),
        });
        var center = input.ContainsKey("rect_center") ? (Vector2)input["rect_center"] : Vector2.Zero;
        var half = input.ContainsKey("rect_half_size") ? (Vector2)input["rect_half_size"] : Vector2.One;
        var push = new[] { (float)Data.Resolution, (float)Data.LayerCount, center.X, center.Y, half.X, half.Y,
            input.ContainsKey("shadow_value") ? (float)input["shadow_value"] : 1.0f, texture != null ? 1.0f : 0.0f };
        _injectOverride.Dispatch(Groups(), Groups(), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set }, CrestRDComputeCs.PackPushConstants(push));
        CrestRDComputeCs.FreeUniformSetDeferred(_device!, set);
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
        image.Convert(Image.Format.Rf);
        var format = new RDTextureFormat { Format = RenderingDevice.DataFormat.R32Sfloat,
            Width = (uint)image.GetWidth(), Height = (uint)image.GetHeight(),
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit };
        var rid = _device.TextureCreate(format, new RDTextureView(), new Array<byte[]> { image.GetData() });
        _textureCache[key] = rid; return rid;
    }

    private Rid FallbackTexture()
    {
        if (_device == null || _fallbackTexture.IsValid) return _fallbackTexture;
        var format = new RDTextureFormat { Format = RenderingDevice.DataFormat.R32Sfloat, Width = 1, Height = 1,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit };
        _fallbackTexture = _device.TextureCreate(format, new RDTextureView(),
            new Array<byte[]> { System.BitConverter.GetBytes(1.0f) });
        return _fallbackTexture;
    }

    private uint Groups() => (uint)Mathf.CeilToInt(Data.Resolution / 8.0f);
    private static RDUniform StorageUniform(uint binding, Rid rid)
    {
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = (int)binding };
        uniform.AddId(rid); return uniform;
    }
    private static byte[] FloatsToBytes(float[] values, int count)
    {
        var bytes = new byte[count * 4];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
