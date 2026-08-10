using Godot;
using System.Collections.Generic;
using Godot.Collections;

namespace Crest.Godot;

/// CPU/GPU-compatible Gerstner component. Each wave uses eight floats:
/// dir.x, dir.z, amplitude, chop amplitude, omega, phase, phase2, k.
[Tool]
public partial class CrestShapeGerstner : Node3D
{
    [Signal] public delegate void WaveDataChangedEventHandler();
    private const int MaxWaveComponents = 1024;
    private Resource? _spectrum;
    private int _componentsPerOctave = 8;
    private int _randomSeed;
    private float _reverseWaveWeight = 0.5f;
    private float _weight = 1.0f;
    private float _attenuationInShallows = 0.95f;
    private bool _dirty = true;
    public int Version { get; private set; }
    public bool IsDirty => _dirty;
    [Export] public Resource? spectrum
    {
        get => _spectrum;
        set
        {
            if (_spectrum == value) return;
            DisconnectSpectrum(_spectrum);
            _spectrum = value;
            ConnectSpectrum(_spectrum);
            MarkDirty();
        }
    }
    [Export(PropertyHint.Range, "1,16,1")] public int components_per_octave
    {
        get => _componentsPerOctave;
        set { value = Mathf.Clamp(value, 1, 16); if (_componentsPerOctave != value) { _componentsPerOctave = value; MarkDirty(); } }
    }
    [Export] public int random_seed
    {
        get => _randomSeed;
        set { if (_randomSeed != value) { _randomSeed = value; MarkDirty(); } }
    }
    [Export] public float[] wave_data { get; set; } = System.Array.Empty<float>();
    [Export(PropertyHint.Range, "0,1,0.01")] public float reverse_wave_weight
    {
        get => _reverseWaveWeight;
        set { value = Mathf.Clamp(value, 0.0f, 1.0f); if (!Mathf.IsEqualApprox(_reverseWaveWeight, value)) { _reverseWaveWeight = value; MarkDirty(); } }
    }
    [Export(PropertyHint.Range, "0,1,0.01")] public float weight
    {
        get => _weight;
        set { value = Mathf.Clamp(value, 0.0f, 1.0f); if (!Mathf.IsEqualApprox(_weight, value)) { _weight = value; MarkDirty(); } }
    }
    [Export(PropertyHint.Range, "0,1,0.01")] public float attenuation_in_shallows
    {
        get => _attenuationInShallows;
        set { value = Mathf.Clamp(value, 0.0f, 1.0f); if (!Mathf.IsEqualApprox(_attenuationInShallows, value)) { _attenuationInShallows = value; MarkDirty(); } }
    }

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
        spectrum ??= new CrestWaveSpectrum();
        ConnectSpectrum(_spectrum);
        if (!Active.Contains(this)) Active.Add(this);
        AddToGroup("crest_shape_generator");
        AddToGroup("crest_shape_generator_cs");
    }

    public override void _ExitTree()
    {
        Active.Remove(this);
        DisconnectSpectrum(_spectrum);
        FreeGpu();
    }

    public void Regenerate(float oceanScale, GodotObject lodTransform)
    {
        if (spectrum == null) return;
        _dirty = false;
        Version++;
        var rng = new RandomNumberGenerator();
        if (random_seed != 0) rng.Seed = (ulong)random_seed; else rng.Randomize();
        Array<Dictionary> generated;
        if (spectrum is CrestWaveSpectrum typedSpectrum)
            generated = typedSpectrum.generate_wave_data(components_per_octave, rng);
        else
        {
            generated = new Array<Dictionary>();
            foreach (var value in spectrum.Call("generate_wave_data", components_per_octave, rng).AsGodotArray())
                if (value.VariantType == Variant.Type.Dictionary) generated.Add(value.AsGodotDictionary());
        }
        var gravity = CrestConstantsCs.Gravity * GetFloat(spectrum, "gravity_scale", 1.0f);
        var chop = GetFloat(spectrum, "chop", 1.6f);
        var chopScales = spectrum.Get("chop_scales").AsFloat32Array();
        var waves = new List<float>();
        foreach (var value in generated)
        {
            var data = value;
            var wavelength = (float)data["wavelength"];
            var amplitude = SpectrumAmplitude(wavelength, gravity) * rng.Randf() * weight;
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
        wave_data = waves.ToArray();
        SortWaves();
        Rebucket(oceanScale, lodTransform);
        FreeWaveBuffer();
        EmitSignal(SignalName.WaveDataChanged);
    }

    public void evaluate(Rid target, GodotObject? depthManager, GodotObject lodTransform,
        double oceanScale, double oceanLevel, double time, bool accumulate = false)
    {
        if (_dirty || wave_data.Length == 0) Regenerate((float)oceanScale, lodTransform);
        if (wave_data.Length == 0) return;
        if (!Mathf.IsEqualApprox(_bucketScale, (float)oceanScale)) Rebucket((float)oceanScale, lodTransform);
        var device = RenderingServer.GetRenderingDevice();
        if (device == null) return;
        _compute ??= CrestRDComputeCs.FromFile(device, "res://addons/crest/shaders/sim/gerstner_eval.glsl");
        if (_compute == null || !_compute.IsValid) return;
        if (!_waveBuffer.IsValid)
            _waveBuffer = device.StorageBufferCreate((uint)(wave_data.Length * 4), FloatsToBytes(wave_data));
        var depthUniform = GetDepthUniform(depthManager, device);
        var set = _compute.MakeUniformSet(new Array<RDUniform>
        {
            StorageUniform(0, _waveBuffer), ImageUniform(1, target), depthUniform,
        });
        var cascade = lodTransform is CrestLodTransformCs typedTransform
            ? typedTransform.CascadeDataCurrent : lodTransform.Get("cascade_data_current").AsFloat32Array();
        var resolution = lodTransform is CrestLodTransformCs typedResolution
            ? typedResolution.LodDataResolution : (int)lodTransform.Get("lod_data_resolution");
        for (var lod = 0; lod < _lodStart.Length; lod++)
        {
            var o = lod * 8;
            var push = new[] { cascade[o], cascade[o + 1], cascade[o + 5], (float)resolution,
                (float)lod, (float)time, reverse_wave_weight, _lodVariance[lod],
                depthManager != null ? attenuation_in_shallows : 0.0f, _lodMedianK[lod], (float)oceanLevel,
                (float)_lodStart[lod], (float)_lodEnd[lod], accumulate ? 1.0f : 0.0f };
            _compute.Dispatch((uint)Mathf.CeilToInt(resolution / 8.0f), (uint)Mathf.CeilToInt(resolution / 8.0f), 1,
                new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set }, CrestRDComputeCs.PackPushConstants(push));
        }
        CrestRDComputeCs.FreeUniformSetDeferred(device, set);
    }

    public Vector3 ComputeDisplacement(Vector2 worldXz, double time, int maxLod = -1)
    {
        var result = Vector3.Zero;
        var count = wave_data.Length / 8;
        if (maxLod >= 0 && _lodEnd.Length > 0)
            count = _lodEnd[Mathf.Min(maxLod, _lodEnd.Length - 1)];
        for (var i = 0; i < count; i++)
        {
            var o = i * 8;
            var direction = new Vector2(wave_data[o], wave_data[o + 1]);
            var amplitude = wave_data[o + 2];
            var chopAmplitude = wave_data[o + 3];
            var omega = wave_data[o + 4];
            var phase = wave_data[o + 5];
            var reversePhase = wave_data[o + 6];
            var waveNumber = wave_data[o + 7];
            var x = waveNumber * direction.Dot(worldXz);
            var a1 = x + phase - omega * (float)time;
            var a2 = x + reversePhase + omega * (float)time;
            var sine = Mathf.Sin(a1) + reverse_wave_weight * Mathf.Sin(a2);
            result.Y += amplitude * (Mathf.Cos(a1) + reverse_wave_weight * Mathf.Cos(a2));
            result.X += chopAmplitude * sine * direction.X;
            result.Z += chopAmplitude * sine * direction.Y;
        }
        return result;
    }

    private void SortWaves()
    {
        var count = wave_data.Length / 8;
        var rows = new List<float[]>(count);
        for (var i = 0; i < count; i++)
        {
            var row = new float[8]; System.Array.Copy(wave_data, i * 8, row, 0, 8); rows.Add(row);
        }
        rows.Sort((a, b) => b[7].CompareTo(a[7]));
        for (var i = 0; i < count; i++) System.Array.Copy(rows[i], 0, wave_data, i * 8, 8);
    }

    private void Rebucket(float oceanScale, GodotObject lodTransform)
    {
        _bucketScale = oceanScale;
        var cascade = lodTransform is CrestLodTransformCs typedTransform
            ? typedTransform.CascadeDataCurrent : lodTransform.Get("cascade_data_current").AsFloat32Array();
        var lodCount = lodTransform is CrestLodTransformCs typedCount
            ? typedCount.LodCount : (int)lodTransform.Get("lod_count");
        _lodStart = new int[lodCount]; _lodEnd = new int[lodCount];
        _lodVariance = new float[lodCount]; _lodMedianK = new float[lodCount];
        var count = wave_data.Length / 8; var index = 0; var variance = 0.0f;
        var min0 = cascade[7] * 0.5f;
        while (index < count && Mathf.Tau / wave_data[index * 8 + 7] < min0) index++;
        var chop = spectrum != null ? GetFloat(spectrum, "chop", 1.6f) : 1.6f;
        for (var lod = 0; lod < lodCount; lod++)
        {
            var maxWavelength = cascade[lod * 8 + 7];
            _lodStart[lod] = index;
            if (lod == lodCount - 1) index = count;
            else while (index < count && Mathf.Tau / wave_data[index * 8 + 7] < maxWavelength) index++;
            _lodEnd[lod] = index; _lodMedianK[lod] = Mathf.Tau / (0.75f * maxWavelength);
            _lodVariance[lod] = variance;
            var minWavelength = maxWavelength * 0.5f;
            if (spectrum != null)
                variance += chop * SpectrumAmplitude(1.5f * minWavelength) / (1.5f * minWavelength);
        }
    }

    private RDUniform GetDepthUniform(GodotObject? manager, RenderingDevice device)
    {
        if (manager != null && manager.HasMethod("make_sampled_uniform"))
        {
            var result = manager.Call("make_sampled_uniform", 2);
            if (result.VariantType == Variant.Type.Object && result.AsGodotObject() is RDUniform uniform) return uniform;
        }
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
    private void ConnectSpectrum(Resource? value)
    {
        if (value == null) return;
        var callable = Callable.From(OnSpectrumChanged);
        if (!value.IsConnected(Resource.SignalName.Changed, callable))
            value.Connect(Resource.SignalName.Changed, callable);
    }
    private void DisconnectSpectrum(Resource? value)
    {
        if (value == null) return;
        var callable = Callable.From(OnSpectrumChanged);
        if (value.IsConnected(Resource.SignalName.Changed, callable))
            value.Disconnect(Resource.SignalName.Changed, callable);
    }
    private static float GetFloat(GodotObject obj, string name, float fallback)
    {
        var value = obj.Get(name); return value.VariantType == Variant.Type.Float ? (float)value : fallback;
    }
    private float SpectrumAmplitude(float wavelength, float gravity = CrestConstantsCs.Gravity)
    {
        if (spectrum is CrestWaveSpectrum typed)
            return typed.get_amplitude(wavelength, components_per_octave, gravity);
        return spectrum != null ? (float)spectrum.Call("get_amplitude", wavelength, components_per_octave, gravity) : 0.0f;
    }
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
