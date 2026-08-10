using Godot;
using System.Collections.Generic;
using Godot.Collections;

namespace Crest.Godot;

/// CPU/GPU-compatible Gerstner component. Each wave uses eight floats:
/// dir.x, dir.z, amplitude, chop amplitude, omega, phase, phase2, k.
[Tool]
public partial class CrestShapeGerstner : Node3D, ICrestShapeGenerator
{
    [Signal] public delegate void WaveDataChangedEventHandler();
    private const int MaxWaveComponents = 1024;
    private CrestWaveSpectrum? _spectrumValue;
    private int _componentsPerOctaveValue = 8;
    private int _randomSeedValue;
    private float _reverseWaveWeightValue = 0.5f;
    private float _weightValue = 1.0f;
    private bool _dirty = true;
    public int Version { get; private set; }
    public bool IsDirty => _dirty;
    [Export] public CrestWaveSpectrum? _spectrum
    {
        get => _spectrumValue;
        set
        {
            if (_spectrumValue == value) return;
            DisconnectSpectrum(_spectrumValue);
            _spectrumValue = value;
            ConnectSpectrum(_spectrumValue);
            MarkDirty();
        }
    }
    [Export] public bool _spectrumFixedAtRuntime { get; set; } = true;
    [Export] public bool _overrideGlobalWindDirection { get; set; }
    [Export(PropertyHint.Range, "-180,180,0.1")] public float _waveDirectionHeadingAngle { get; set; }
    [Export] public bool _overrideGlobalWindSpeed { get; set; }
    [Export(PropertyHint.Range, "0,150,0.1")] public float _windSpeed { get; set; } = 20.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _respectShallowWaterAttenuation { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _weight
    {
        get => _weightValue;
        set { value = Mathf.Clamp(value, 0.0f, 1.0f); if (!Mathf.IsEqualApprox(_weightValue, value)) { _weightValue = value; MarkDirty(); } }
    }
    [Export] public int _blendMode { get; set; }
    [Export] public int _queue { get; set; }
    [Export] public int _resolution { get; set; } = 128;
    [Export] public float _featherWidth { get; set; }
    [Export] public bool _overrideSplineSettings { get; set; }
    [Export] public float _radius { get; set; } = 50.0f;
    [Export] public int _subdivisions { get; set; } = 1;
    [Export] public float _featherWaveStart { get; set; } = 0.1f;
    [Export] public int _version { get; set; } = 1;
    [Export] public bool _swell { get; set; }
    [Export(PropertyHint.Range, "1,16,1")] public int _componentsPerOctave
    {
        get => _componentsPerOctaveValue;
        set { value = Mathf.Clamp(value, 1, 16); if (_componentsPerOctaveValue != value) { _componentsPerOctaveValue = value; MarkDirty(); } }
    }
    [Export] public int _randomSeed
    {
        get => _randomSeedValue;
        set { if (_randomSeedValue != value) { _randomSeedValue = value; MarkDirty(); } }
    }
    public float[] WaveData { get; set; } = System.Array.Empty<float>();
    [Export(PropertyHint.Range, "0,1,0.01")] public float _reverseWaveWeight
    {
        get => _reverseWaveWeightValue;
        set { value = Mathf.Clamp(value, 0.0f, 1.0f); if (!Mathf.IsEqualApprox(_reverseWaveWeightValue, value)) { _reverseWaveWeightValue = value; MarkDirty(); } }
    }
    [Export] public CrestShapeDebugFields _debug { get; set; } = new();
    private int[] _lodStart = System.Array.Empty<int>();
    private int[] _lodEnd = System.Array.Empty<int>();
    private float[] _lodVariance = System.Array.Empty<float>();
    private float[] _lodMedianK = System.Array.Empty<float>();
    private float _bucketScale = -1.0f;
    private Rid _waveBuffer;
    private CrestRDComputeCs? _compute;
    private CrestLodDataMgrCs? _fallbackDepth;

    private static readonly List<CrestShapeGerstner> Active = new();
    public static IReadOnlyList<CrestShapeGerstner> ActiveShapes => Active;

    public override void _EnterTree()
    {
        _spectrum ??= new CrestWaveSpectrum();
        ConnectSpectrum(_spectrumValue);
        if (!Active.Contains(this)) Active.Add(this);
        AddToGroup("crest_shape_generator");
        AddToGroup("crest_shape_generator_cs");
    }

    public override void _ExitTree()
    {
        Active.Remove(this);
        DisconnectSpectrum(_spectrumValue);
        FreeGpu();
    }

    public void Regenerate(float oceanScale, CrestLodTransformCs lodTransform)
    {
        if (_spectrum == null) return;
        _dirty = false;
        Version++;
        var rng = new RandomNumberGenerator();
        if (_randomSeed != 0) rng.Seed = (ulong)_randomSeed; else rng.Randomize();
        var generated = _spectrum.generate_wave_data(_componentsPerOctave, rng, WindDirectionAngle());
        var gravity = CrestConstantsCs.Gravity * _spectrum._gravityScale;
        var chop = _spectrum._chop;
        var chopScales = _spectrum._chopScales;
        var waves = new List<float>();
        foreach (var value in generated)
        {
            var data = value;
            var wavelength = (float)data["wavelength"];
            var amplitude = SpectrumAmplitude(wavelength, gravity) * rng.Randf() * _weight;
            if (amplitude < 0.001f) continue;
            var octave = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(wavelength) / Mathf.Log(2.0f)) + 4, 0, 13);
            var k = Mathf.Tau / wavelength;
            var angle = Mathf.DegToRad((float)data["angle_deg"]);
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var omega = k * Mathf.Sqrt(wavelength * gravity / Mathf.Tau);
            var chopScale = octave < chopScales.Length ? chopScales[octave] : 1.0f;
            var phase = (float)data["phase"];
            waves.AddRange(new[] { direction.X, direction.Y, amplitude, -chopScale * chop * amplitude,
                omega, phase, Mathf.PosMod(phase + rng.Randf() * Mathf.Tau * 0.13f, Mathf.Tau), k });
            if (waves.Count / 8 >= MaxWaveComponents) break;
        }
        WaveData = waves.ToArray();
        SortWaves();
        Rebucket(oceanScale, lodTransform);
        FreeWaveBuffer();
        EmitSignal(SignalName.WaveDataChanged);
    }

    public void Evaluate(Rid target, CrestSeaFloorDepthManagerCs? depthManager, CrestLodTransformCs lodTransform,
        double oceanScale, double oceanLevel, double time, bool accumulate = false)
    {
        if (_dirty || WaveData.Length == 0) Regenerate((float)oceanScale, lodTransform);
        if (WaveData.Length == 0) return;
        if (!Mathf.IsEqualApprox(_bucketScale, (float)oceanScale)) Rebucket((float)oceanScale, lodTransform);
        var device = RenderingServer.GetRenderingDevice();
        if (device == null) return;
        _compute ??= CrestRDComputeCs.FromFile(device, "res://addons/crest/shaders/sim/gerstner_eval.glsl");
        if (_compute == null || !_compute.IsValid) return;
        if (!_waveBuffer.IsValid)
            _waveBuffer = device.StorageBufferCreate((uint)(WaveData.Length * 4), FloatsToBytes(WaveData));
        var depthUniform = GetDepthUniform(depthManager, device);
        var set = _compute.MakeUniformSet(new Array<RDUniform>
        {
            StorageUniform(0, _waveBuffer), ImageUniform(1, target), depthUniform,
        });
        var cascade = lodTransform.CascadeDataCurrent;
        var resolution = lodTransform.LodDataResolution;
        for (var lod = 0; lod < _lodStart.Length; lod++)
        {
            var o = lod * 8;
            var push = new[] { cascade[o], cascade[o + 1], cascade[o + 5], (float)resolution,
                (float)lod, (float)time, _swell ? 0.0f : _reverseWaveWeight, _lodVariance[lod],
                depthManager != null ? _respectShallowWaterAttenuation : 0.0f, _lodMedianK[lod], (float)oceanLevel,
                (float)_lodStart[lod], (float)_lodEnd[lod], accumulate ? 1.0f : 0.0f };
            _compute.Dispatch((uint)Mathf.CeilToInt(resolution / 8.0f), (uint)Mathf.CeilToInt(resolution / 8.0f), 1,
                new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set }, CrestRDComputeCs.PackPushConstants(push));
        }
        CrestRDComputeCs.FreeUniformSetDeferred(device, set);
    }

    public Vector3 ComputeDisplacement(Vector2 worldXz, double time, int maxLod = -1)
    {
        var result = Vector3.Zero;
        var count = WaveData.Length / 8;
        if (maxLod >= 0 && _lodEnd.Length > 0)
            count = _lodEnd[Mathf.Min(maxLod, _lodEnd.Length - 1)];
        for (var i = 0; i < count; i++)
        {
            var o = i * 8;
            var direction = new Vector2(WaveData[o], WaveData[o + 1]);
            var amplitude = WaveData[o + 2];
            var chopAmplitude = WaveData[o + 3];
            var omega = WaveData[o + 4];
            var phase = WaveData[o + 5];
            var reversePhase = WaveData[o + 6];
            var waveNumber = WaveData[o + 7];
            var x = waveNumber * direction.Dot(worldXz);
            var a1 = x + phase - omega * (float)time;
            var a2 = x + reversePhase + omega * (float)time;
            var reverseWeight = _swell ? 0.0f : _reverseWaveWeight;
            var sine = Mathf.Sin(a1) + reverseWeight * Mathf.Sin(a2);
            result.Y += amplitude * (Mathf.Cos(a1) + reverseWeight * Mathf.Cos(a2));
            result.X += chopAmplitude * sine * direction.X;
            result.Z += chopAmplitude * sine * direction.Y;
        }
        return result;
    }

    private void SortWaves()
    {
        var count = WaveData.Length / 8;
        var rows = new List<float[]>(count);
        for (var i = 0; i < count; i++)
        {
            var row = new float[8]; System.Array.Copy(WaveData, i * 8, row, 0, 8); rows.Add(row);
        }
        rows.Sort((a, b) => b[7].CompareTo(a[7]));
        for (var i = 0; i < count; i++) System.Array.Copy(rows[i], 0, WaveData, i * 8, 8);
    }

    private void Rebucket(float oceanScale, CrestLodTransformCs lodTransform)
    {
        _bucketScale = oceanScale;
        var cascade = lodTransform.CascadeDataCurrent;
        var lodCount = lodTransform is CrestLodTransformCs typedCount
            ? typedCount.LodCount : (int)lodTransform.Get("lod_count");
        _lodStart = new int[lodCount]; _lodEnd = new int[lodCount];
        _lodVariance = new float[lodCount]; _lodMedianK = new float[lodCount];
        var count = WaveData.Length / 8; var index = 0; var variance = 0.0f;
        var min0 = cascade[7] * 0.5f;
        while (index < count && Mathf.Tau / WaveData[index * 8 + 7] < min0) index++;
        var chop = _spectrum?._chop ?? 1.6f;
        for (var lod = 0; lod < lodCount; lod++)
        {
            var maxWavelength = cascade[lod * 8 + 7];
            _lodStart[lod] = index;
            if (lod == lodCount - 1) index = count;
            else while (index < count && Mathf.Tau / WaveData[index * 8 + 7] < maxWavelength) index++;
            _lodEnd[lod] = index; _lodMedianK[lod] = Mathf.Tau / (0.75f * maxWavelength);
            _lodVariance[lod] = variance;
            var minWavelength = maxWavelength * 0.5f;
            if (_spectrum != null)
                variance += chop * SpectrumAmplitude(1.5f * minWavelength) / (1.5f * minWavelength);
        }
    }

    private RDUniform GetDepthUniform(CrestSeaFloorDepthManagerCs? manager, RenderingDevice device)
    {
        if (manager != null) return manager.Data.MakeSampledUniform(2);
        _fallbackDepth ??= new CrestLodDataMgrCs();
        if (_fallbackDepth.Device == null)
            _fallbackDepth.InitSim(1, 2, RenderingDevice.DataFormat.R32G32Sfloat, false);
        return _fallbackDepth.MakeSampledUniform(2);
    }
    private void FreeWaveBuffer()
    {
        var device = RenderingServer.GetRenderingDevice();
        if (device != null && _waveBuffer.IsValid) CrestRDComputeCs.FreeRidDeferred(device, _waveBuffer);
        _waveBuffer = new Rid();
    }
    private void FreeGpu()
    {
        FreeWaveBuffer(); _compute?.DisposeRid(); _fallbackDepth?.FreeRids(); _fallbackDepth = null;
    }
    private void OnSpectrumChanged() => MarkDirty();
    private void MarkDirty() => _dirty = true;
    private void ConnectSpectrum(CrestWaveSpectrum? value)
    {
        if (value == null) return;
        var callable = Callable.From(OnSpectrumChanged);
        if (!value.IsConnected(Resource.SignalName.Changed, callable))
            value.Connect(Resource.SignalName.Changed, callable);
    }
    private void DisconnectSpectrum(CrestWaveSpectrum? value)
    {
        if (value == null) return;
        var callable = Callable.From(OnSpectrumChanged);
        if (value.IsConnected(Resource.SignalName.Changed, callable))
            value.Disconnect(Resource.SignalName.Changed, callable);
    }
    private float SpectrumAmplitude(float wavelength, float gravity = CrestConstantsCs.Gravity)
    {
        return _spectrum?.get_amplitude(wavelength, _componentsPerOctave,
            WindSpeedMetersPerSecond(), gravity) ?? 0.0f;
    }
    private float WindDirectionAngle() => _overrideGlobalWindDirection
        ? _waveDirectionHeadingAngle
        : CrestOceanRendererFacade.Instance?._globalWindDirectionAngle ?? _waveDirectionHeadingAngle;
    private float WindSpeedMetersPerSecond() => (_overrideGlobalWindSpeed
        ? _windSpeed
        : CrestOceanRendererFacade.Instance?._globalWindSpeed ?? _windSpeed) / 3.6f;
    private static RDUniform StorageUniform(uint binding, Rid rid)
    {
        var u = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = (int)binding }; u.AddId(rid); return u;
    }
    private static RDUniform ImageUniform(uint binding, Rid rid)
    {
        var u = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = (int)binding }; u.AddId(rid); return u;
    }
    private static byte[] FloatsToBytes(float[] values)
    {
        var bytes = new byte[values.Length * 4]; System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length); return bytes;
    }
}
