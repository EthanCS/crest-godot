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
    private Rid _casterBuffer;

    public void init_mgr(int resolution, int layers, Resource? source = null)
    {
        Settings = source as CrestSimSettingsShadow ?? new CrestSimSettingsShadow();
        Data.InitSim(resolution, layers, RenderingDevice.DataFormat.R8G8Unorm, true, new Color(1, 1, 0, 0));
        _device = Data.Device;
        if (_device != null)
        {
            _update = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/update_shadow.glsl");
            _casterBuffer = _device.StorageBufferCreate(MaxCasters * 16u);
        }
    }

    public void update_sim(double delta, GodotObject? lodTransform, Rid cascadeCurrent,
        Rid cascadeSource, GodotObject? animatedWaves, double oceanLevel,
        double lodChange, double time, Array casters)
    {
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
            SampledFromManager(animatedWaves, 5),
        };
        var set = _update.MakeUniformSet(uniforms);
        var push = new[] { (float)Data.Resolution, (float)Data.LayerCount, (float)delta, (float)lodChange,
            (float)count, Settings.jitter_diameter_soft, Settings.current_frame_weight_soft,
            Settings.jitter_diameter_hard, Settings.current_frame_weight_hard,
            light_dir.X, light_dir.Y, light_dir.Z, (float)time, (float)oceanLevel, 1.0f };
        _update.Dispatch(Groups(), Groups(), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set }, CrestRDComputeCs.PackPushConstants(push));
        CrestRDComputeCs.FreeUniformSetDeferred(_device, set);
        Data.SwapTargets();
    }

    public void free_rids()
    {
        _update?.DisposeRid();
        if (_device != null && _casterBuffer.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, _casterBuffer);
        _casterBuffer = new Rid(); Data.FreeRids();
    }

    private uint Groups() => (uint)Mathf.CeilToInt(Data.Resolution / 8.0f);
    private static RDUniform StorageUniform(uint binding, Rid rid)
    {
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = (int)binding };
        uniform.AddId(rid); return uniform;
    }
    private RDUniform SampledFromManager(GodotObject? manager, uint binding)
    {
        if (manager != null && manager.HasMethod("make_sampled_uniform"))
        {
            var result = manager.Call("make_sampled_uniform", (int)binding);
            if (result.VariantType == Variant.Type.Object && result.AsGodotObject() is RDUniform uniform) return uniform;
        }
        return Data.MakeSampledUniform(binding);
    }
    private static byte[] FloatsToBytes(float[] values, int count)
    {
        var bytes = new byte[count * 4];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
