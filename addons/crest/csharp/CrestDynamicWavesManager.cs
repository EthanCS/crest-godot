using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# dynamic wave-equation manager with fixed substeps and sphere injection.
[GlobalClass]
public partial class CrestDynamicWavesManagerCs : RefCounted
{
    public int SphereDispatchCount { get; private set; }
    private const int MaxSpheres = 256;
    public CrestLodDataMgrCs Data { get; } = new();
    public Texture2DArrayRD texture_array => Data.TextureArray;
    public CrestSimSettingsWave Settings { get; private set; } = new();
    private RenderingDevice? _device;
    private CrestRDComputeCs? _update;
    private CrestRDComputeCs? _inject;
    private CrestLodDataMgrCs? _fallback;
    private Rid _sphereBuffer;
    private float _timeToSimulate;

    public void init_mgr(int resolution, int layers, Resource? source = null)
    {
        Settings = source as CrestSimSettingsWave ?? new CrestSimSettingsWave();
        Data.InitSim(resolution, layers, RenderingDevice.DataFormat.R16G16Sfloat, true);
        _device = Data.Device;
        if (_device != null)
        {
            _update = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/update_dyn_waves.glsl");
            _inject = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/inject_dyn_waves.glsl");
            _sphereBuffer = _device.StorageBufferCreate(MaxSpheres * 32u);
            _fallback = new CrestLodDataMgrCs();
            _fallback.InitSim(1, 2, RenderingDevice.DataFormat.R16G16Sfloat, false);
        }
    }

    public Variant make_sampled_uniform(int binding) => Data.MakeSampledUniform((uint)binding);
    public Variant current_texture() => Data.CurrentTexture();

    public void update_sim(double delta, GodotObject? lodTransform, Rid cascadeCurrent,
        Rid cascadeSource, GodotObject? flowManager, GodotObject? depthManager,
        double oceanLevel, double gravity, double lodChange, Array spheres)
    {
        if (_device == null || _update == null || !_update.IsValid) return;
        var frequency = Mathf.Max(Settings.simulation_frequency, 1.0f);
        _timeToSimulate += (float)delta;
        var count = Mathf.FloorToInt(_timeToSimulate * frequency);
        var dt = 1.0f / frequency;
        if (count == 0) { count = 1; dt = 0.0f; }
        _timeToSimulate -= count * dt;
        for (var i = 0; i < count; i++)
        {
            DispatchUpdate(dt, cascadeCurrent, cascadeSource, flowManager, depthManager,
                (float)oceanLevel, (float)gravity, (float)lodChange, i == 0);
            Data.SwapTargets();
            if (dt > 0.0f && (spheres.Count > 0 || CrestSphereWaterInteraction.ActiveInteractions.Count > 0))
                DispatchSpheres(dt, cascadeCurrent, spheres);
        }
    }

    public void free_rids()
    {
        _update?.DisposeRid(); _inject?.DisposeRid();
        if (_device != null && _sphereBuffer.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, _sphereBuffer);
        _sphereBuffer = new Rid(); _fallback?.FreeRids(); _fallback = null; Data.FreeRids();
    }

    private void DispatchUpdate(float dt, Rid current, Rid source, GodotObject? flow, GodotObject? depth,
        float oceanLevel, float gravity, float lodChange, bool useSource)
    {
        var set = _update!.MakeUniformSet(new Array<RDUniform>
        {
            StorageUniform(0, current), StorageUniform(1, source), Data.MakeSampledUniform(2),
            Data.MakeImageUniform(3), SampledFromManager(flow, 4), SampledFromManager(depth, 5),
        });
        var push = new[] { (float)Data.Resolution, (float)Data.LayerCount, dt,
            gravity * Settings.gravity_multiplier, Settings.damping, Settings.courant_number,
            Settings.attenuation_in_shallows, lodChange, oceanLevel, useSource ? 1.0f : 0.0f };
        _update.Dispatch(Groups(), Groups(), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set }, CrestRDComputeCs.PackPushConstants(push));
        CrestRDComputeCs.FreeUniformSetDeferred(_device!, set);
    }

    private void DispatchSpheres(float dt, Rid cascadeCurrent, Array spheres)
    {
        if (_inject == null || !_inject.IsValid) return;
        var capacity = Mathf.Min(spheres.Count + CrestSphereWaterInteraction.ActiveInteractions.Count, MaxSpheres);
        var values = new float[capacity * 8];
        var count = 0;
        foreach (var sphere in CrestSphereWaterInteraction.ActiveInteractions)
        {
            if (count >= MaxSpheres || !sphere.TryGetInjection(out var position, out var velocity,
                out var radius, out var weight)) continue;
            WriteSphere(values, count++, position, velocity, radius, weight);
        }
        foreach (var value in spheres)
        {
            if (count >= MaxSpheres || value.VariantType != Variant.Type.Dictionary) break;
            var sphere = value.AsGodotDictionary();
            if (!sphere.ContainsKey("pos") || !sphere.ContainsKey("vel")) continue;
            var position = (Vector2)sphere["pos"]; var velocity = (Vector3)sphere["vel"];
            WriteSphere(values, count++, position, velocity,
                sphere.ContainsKey("radius") ? (float)sphere["radius"] : 1.0f,
                sphere.ContainsKey("weight") ? (float)sphere["weight"] : 1.0f);
        }
        if (count == 0) return;
        _device!.BufferUpdate(_sphereBuffer, 0, (uint)(count * 32), FloatsToBytes(values, count * 8));
        var set = _inject.MakeUniformSet(new Array<RDUniform>
        {
            StorageUniform(0, cascadeCurrent), StorageUniform(1, _sphereBuffer), Data.MakeImageUniform(2, false),
        });
        var push = new[] { (float)Data.Resolution, (float)Data.LayerCount, (float)count, dt, 0.5f, 1.55f, 0.109f };
        _inject.Dispatch(Groups(), Groups(), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set }, CrestRDComputeCs.PackPushConstants(push));
        SphereDispatchCount++;
        CrestRDComputeCs.FreeUniformSetDeferred(_device, set);
    }

    private RDUniform SampledFromManager(GodotObject? manager, uint binding)
    {
        if (manager != null && manager.HasMethod("make_sampled_uniform"))
        {
            var result = manager.Call("make_sampled_uniform", (int)binding);
            if (result.VariantType == Variant.Type.Object && result.AsGodotObject() is RDUniform uniform) return uniform;
        }
        return _fallback!.MakeSampledUniform(binding);
    }
    private uint Groups() => (uint)Mathf.CeilToInt(Data.Resolution / 8.0f);
    private static RDUniform StorageUniform(uint binding, Rid rid)
    {
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = (int)binding };
        uniform.AddId(rid); return uniform;
    }
    private static byte[] FloatsToBytes(float[] values, int count)
    {
        var bytes = new byte[count * 4]; System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length); return bytes;
    }

    private static void WriteSphere(float[] values, int index, Vector2 position,
        Vector3 velocity, float radius, float weight)
    {
        var offset = index * 8;
        values[offset] = position.X; values[offset + 1] = position.Y;
        values[offset + 2] = radius; values[offset + 3] = weight;
        values[offset + 4] = velocity.X; values[offset + 5] = velocity.Y;
        values[offset + 6] = velocity.Z;
    }
}
