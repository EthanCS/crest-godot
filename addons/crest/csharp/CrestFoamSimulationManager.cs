using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# foam simulation manager with fixed-step scheduling, prewarm and GPU input injection.
[GlobalClass]
public partial class CrestFoamSimulationManagerCs : RefCounted
{
    [Export] public CrestSimSettingsFoam settings { get; set; } = new();
    public CrestLodDataMgrCs Data { get; } = new();
    public Texture2DArrayRD texture_array => Data.TextureArray;
    public float TimeToSimulate { get; private set; }
    public bool NeedsPrewarm { get; private set; } = true;
    public int LastSubstepCount { get; private set; }
    public float LastSubstepDelta { get; private set; }
    private RenderingDevice? _device;
    private CrestRDComputeCs? _update;
    private CrestRDComputeCs? _inject;
    private readonly System.Collections.Generic.Dictionary<ulong, Rid> _textureCache = new();
    private Rid _fallbackTexture;

    public void InitManager(int resolution, int layers, CrestSimSettingsFoam? source = null)
    {
        settings = source ?? new CrestSimSettingsFoam();
        Data.InitSim(resolution, layers, RenderingDevice.DataFormat.R16Sfloat, true);
        _device = Data.Device;
        if (_device != null)
        {
            _update = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/update_foam.glsl");
            _inject = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/inject_foam.glsl");
        }
        TimeToSimulate = 0.0f;
        NeedsPrewarm = true;
        LastSubstepCount = 0;
        LastSubstepDelta = 0.0f;
    }

    // Compatibility spelling retained for serialized/script-facing APIs.
    public void init_mgr(int resolution, int layers, Resource? source = null)
    {
        InitManager(resolution, layers, source as CrestSimSettingsFoam);
    }

    public void NotifyTeleport() => NeedsPrewarm = true;

    /// Advances the fixed-frequency simulation clock and returns the number
    /// of dispatches the renderer should issue this frame.
    public int UpdateSchedule(float delta)
    {
        var frequency = Mathf.Max(settings.simulation_frequency, 1.0f);
        TimeToSimulate += Mathf.Max(delta, 0.0f);
        var substeps = Mathf.FloorToInt(TimeToSimulate * frequency);
        var step = 1.0f / frequency;
        if (substeps == 0)
        {
            substeps = 1;
            LastSubstepDelta = 0.0f;
        }
        else
        {
            TimeToSimulate -= substeps * step;
            LastSubstepDelta = step;
        }
        LastSubstepCount = substeps;
        return substeps;
    }

    /// Dispatches one update step using the same bindings and float-only push
    /// constants as the compute shader contract. Returns false when no GPU is present.
    public bool DispatchUpdate(float dt, Rid cascadeCurrent, Rid cascadeSource,
        CrestLodDataMgrCs? flow, CrestLodDataMgrCs? animatedWaves,
        CrestLodDataMgrCs? depth, float oceanLevel, float lodChange,
        bool useSourceTransforms)
    {
        if (_device == null || _update == null || !_update.IsValid)
            return false;
        var uniforms = new Array<RDUniform>
        {
            StorageUniform(0, cascadeCurrent), StorageUniform(1, cascadeSource),
            Data.MakeSampledUniform(2), Data.MakeImageUniform(3),
            (flow ?? Data).MakeSampledUniform(4),
            (animatedWaves ?? Data).MakeSampledUniform(5),
            (depth ?? Data).MakeSampledUniform(6),
        };
        var set = _update.MakeUniformSet(uniforms);
        var values = new[]
        {
            (float)Data.Resolution, (float)Data.LayerCount, dt, lodChange, oceanLevel,
            useSourceTransforms ? 1.0f : 0.0f, settings.foam_fade_rate,
            settings.wave_foam_strength, settings.wave_foam_coverage,
            (float)settings.filter_waves, settings.shoreline_foam_max_depth,
            settings.shoreline_foam_strength, NeedsPrewarm && settings.prewarm ? 1.0f : 0.0f,
        };
        _update.Dispatch((uint)Mathf.CeilToInt(Data.Resolution / 8.0f),
            (uint)Mathf.CeilToInt(Data.Resolution / 8.0f), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set },
            CrestRDComputeCs.PackPushConstants(values));
        CrestRDComputeCs.FreeUniformSetDeferred(_device, set);
        Data.SwapTargets();
        NeedsPrewarm = false;
        return true;
    }

    public int DispatchInputs(Rid cascadeCurrent, Array<Dictionary> inputs, float time)
    {
        if (_device == null || _inject == null || !_inject.IsValid)
            return 0;
        var dispatched = 0;
        foreach (var input in inputs)
        {
            var texture = GetTexture(input);
            var textureUniform = new RDUniform
            {
                UniformType = RenderingDevice.UniformType.SamplerWithTexture,
                Binding = 1,
            };
            textureUniform.AddId(Data.Sampler);
            textureUniform.AddId(GetTextureRid(texture));
            var uniforms = new Array<RDUniform>
            {
                StorageUniform(0, cascadeCurrent), textureUniform,
                Data.MakeImageUniform(2, false),
            };
            var set = _inject.MakeUniformSet(uniforms);
            var center = input.ContainsKey("rect_center") ? (Vector2)input["rect_center"] : Vector2.Zero;
            var half = input.ContainsKey("rect_half_size") ? (Vector2)input["rect_half_size"] : Vector2.One;
            var values = new[]
            {
                (float)Data.Resolution, (float)Data.LayerCount,
                center.X, center.Y, half.X, half.Y,
                input.ContainsKey("strength") ? (float)input["strength"] : 1.0f,
                input.ContainsKey("mode") ? (float)input["mode"] : 0.0f,
                texture != null ? 1.0f : 0.0f, time,
            };
            _inject.Dispatch((uint)Mathf.CeilToInt(Data.Resolution / 8.0f),
                (uint)Mathf.CeilToInt(Data.Resolution / 8.0f), (uint)Data.LayerCount,
                new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set },
                CrestRDComputeCs.PackPushConstants(values));
            CrestRDComputeCs.FreeUniformSetDeferred(_device, set);
            dispatched++;
        }
        return dispatched;
    }

    public void update_sim(double delta, GodotObject? lodTransform, Rid cascadeCurrent,
        Rid cascadeSource, GodotObject? flow, GodotObject? animatedWaves,
        GodotObject? depth, double oceanLevel, double lodChange)
    {
        var count = UpdateSchedule((float)delta);
        for (var i = 0; i < count; i++)
        {
            DispatchUpdateDynamic(LastSubstepDelta, cascadeCurrent, cascadeSource,
                flow, animatedWaves, depth, (float)oceanLevel, (float)lodChange, i == 0);
        }
    }

    public void inject_inputs(GodotObject? lodTransform, Rid cascadeCurrent, Array inputs, double time)
    {
        var typed = new Array<Dictionary>();
        foreach (var value in inputs)
            if (value.VariantType == Variant.Type.Dictionary)
                typed.Add(value.AsGodotDictionary());
        DispatchInputs(cascadeCurrent, typed, (float)time);
    }

    public void notify_teleport() => NotifyTeleport();
    public void free_rids() => FreeRids();

    public void FreeRids()
    {
        _update?.DisposeRid();
        _inject?.DisposeRid();
        if (_device != null)
        {
            foreach (var rid in _textureCache.Values)
                if (rid.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, rid);
            if (_fallbackTexture.IsValid)
                CrestRDComputeCs.FreeRidDeferred(_device, _fallbackTexture);
        }
        _textureCache.Clear();
        _fallbackTexture = new Rid();
        Data.FreeRids();
    }

    private static Texture2D? GetTexture(Dictionary input)
    {
        if (!input.ContainsKey("texture"))
            return null;
        var value = input["texture"];
        return value.VariantType == Variant.Type.Object ? value.AsGodotObject() as Texture2D : null;
    }

    private Rid GetTextureRid(Texture2D? texture)
    {
        if (_device == null || texture == null)
            return GetFallbackTexture();
        var key = texture.GetInstanceId();
        if (_textureCache.TryGetValue(key, out var cached))
            return cached;
        var image = texture.GetImage();
        if (image == null || image.IsEmpty())
            return GetFallbackTexture();
        image.Convert(Image.Format.Rgbaf);
        var format = new RDTextureFormat
        {
            Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat,
            Width = (uint)image.GetWidth(), Height = (uint)image.GetHeight(),
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit |
                RenderingDevice.TextureUsageBits.CanUpdateBit,
        };
        var rid = _device.TextureCreate(format, new RDTextureView(), new Array<byte[]> { image.GetData() });
        _textureCache[key] = rid;
        return rid;
    }

    private Rid GetFallbackTexture()
    {
        if (_device == null || _fallbackTexture.IsValid)
            return _fallbackTexture;
        var format = new RDTextureFormat
        {
            Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat,
            Width = 1, Height = 1,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit |
                RenderingDevice.TextureUsageBits.CanUpdateBit,
        };
        var data = CrestRDComputeCs.PackPushConstants(new[] { 1.0f, 1.0f, 1.0f, 1.0f });
        _fallbackTexture = _device.TextureCreate(format, new RDTextureView(), new Array<byte[]> { data });
        return _fallbackTexture;
    }

    private static RDUniform StorageUniform(uint binding, Rid rid)
    {
        var uniform = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = (int)binding,
        };
        uniform.AddId(rid);
        return uniform;
    }

    private bool DispatchUpdateDynamic(float dt, Rid cascadeCurrent, Rid cascadeSource,
        GodotObject? flow, GodotObject? animatedWaves, GodotObject? depth,
        float oceanLevel, float lodChange, bool useSourceTransforms)
    {
        if (_device == null || _update == null || !_update.IsValid)
            return false;
        var uniforms = new Array<RDUniform>
        {
            StorageUniform(0, cascadeCurrent), StorageUniform(1, cascadeSource),
            Data.MakeSampledUniform(2), Data.MakeImageUniform(3),
            SampledFromManager(flow, 4), SampledFromManager(animatedWaves, 5),
            SampledFromManager(depth, 6),
        };
        var set = _update.MakeUniformSet(uniforms);
        var values = new[]
        {
            (float)Data.Resolution, (float)Data.LayerCount, dt, lodChange, oceanLevel,
            useSourceTransforms ? 1.0f : 0.0f, settings.foam_fade_rate,
            settings.wave_foam_strength, settings.wave_foam_coverage,
            (float)settings.filter_waves, settings.shoreline_foam_max_depth,
            settings.shoreline_foam_strength, NeedsPrewarm && settings.prewarm ? 1.0f : 0.0f,
        };
        _update.Dispatch((uint)Mathf.CeilToInt(Data.Resolution / 8.0f),
            (uint)Mathf.CeilToInt(Data.Resolution / 8.0f), (uint)Data.LayerCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set },
            CrestRDComputeCs.PackPushConstants(values));
        CrestRDComputeCs.FreeUniformSetDeferred(_device, set);
        Data.SwapTargets();
        NeedsPrewarm = false;
        return true;
    }

    private RDUniform SampledFromManager(GodotObject? manager, uint binding)
    {
        if (manager != null && manager.HasMethod("make_sampled_uniform"))
        {
            var result = manager.Call("make_sampled_uniform", (int)binding);
            if (result.VariantType == Variant.Type.Object && result.AsGodotObject() is RDUniform uniform)
                return uniform;
        }
        return Data.MakeSampledUniform(binding);
    }
}
