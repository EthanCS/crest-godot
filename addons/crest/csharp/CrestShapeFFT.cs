using Godot;

namespace Crest.Godot;

/// FFT wave generator that maps the 16 tiled spectral bands into the
/// view-following animated-wave cascades.
[Tool]
public partial class CrestShapeFFT : Node3D
{
    private CrestWaveSpectrum? _spectrum;
    private int _resolution = 128;
    private CrestFFTComputeCs? _fft;
    private float _turbulence = 0.145f;
    private int _randomSeed;
    private bool _spectrumDirty = true;
    private bool _reinitialize = true;
    private Rid _sliceMapBuffer;
    private float _lastScale = -1.0f;
    public int SampleDispatchCount => _fft?.SampleDispatchCount ?? 0;

    [Export]
    public CrestWaveSpectrum? spectrum
    {
        get => _spectrum;
        set
        {
            if (_spectrum == value) return;
            DisconnectSpectrum(_spectrum);
            _spectrum = value;
            ConnectSpectrum(_spectrum);
            _spectrumDirty = true;
        }
    }

    [Export(PropertyHint.Range, "16,512,1")]
    public int resolution
    {
        get => _resolution;
        set
        {
            var exponent = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(Mathf.Max(value, 16)) /
                Mathf.Log(2.0f)), 4, 9);
            var clamped = 1 << exponent;
            if (_resolution == clamped) return;
            _resolution = clamped;
            _reinitialize = true;
            _spectrumDirty = true;
        }
    }

    [Export(PropertyHint.Range, "0,1,0.01")] public float weight { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,1,0.001")]
    public float turbulence
    {
        get => _turbulence;
        set
        {
            var clamped = Mathf.Clamp(value, 0.0f, 1.0f);
            if (Mathf.IsEqualApprox(_turbulence, clamped)) return;
            _turbulence = clamped;
            _spectrumDirty = true;
        }
    }

    [Export]
    public int random_seed
    {
        get => _randomSeed;
        set
        {
            if (_randomSeed == value) return;
            _randomSeed = value;
            _spectrumDirty = true;
        }
    }

    public override void _EnterTree()
    {
        ConnectSpectrum(_spectrum);
        AddToGroup("crest_shape_generator");
        AddToGroup("crest_shape_generator_cs");
    }

    public override void _ExitTree()
    {
        DisconnectSpectrum(_spectrum);
        _fft?.FreeRids();
        _fft = null;
        var device = RenderingServer.GetRenderingDevice();
        if (device != null && _sliceMapBuffer.IsValid)
            CrestRDComputeCs.FreeRidDeferred(device, _sliceMapBuffer);
        _sliceMapBuffer = new Rid();
    }

    public void evaluate(Rid waveBuffer, GodotObject? depthManager, GodotObject lodTransform,
        double oceanScale, double oceanLevel, double time, bool accumulate = false)
    {
        _ = depthManager;
        _ = oceanLevel;
        var device = RenderingServer.GetRenderingDevice();
        if (device == null) return;
        spectrum ??= new CrestWaveSpectrum();

        if (_reinitialize || _fft == null)
        {
            _fft?.FreeRids();
            _fft = new CrestFFTComputeCs();
            if (!_fft.Initialize(_resolution))
            {
                _fft = null;
                return;
            }
            _reinitialize = false;
            _spectrumDirty = true;
            _lastScale = -1.0f;
        }

        if (_spectrumDirty)
        {
            var angle = Mathf.DegToRad(spectrum.wind_direction_angle);
            _fft.RebuildSpectrum(spectrum, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)),
                turbulence, random_seed);
            _spectrumDirty = false;
        }

        _fft.AdvanceTime((float)time * spectrum.gravity_scale, spectrum.gravity_scale, spectrum.chop);
        var typedTransform = lodTransform as CrestLodTransformCs;
        var lodCount = typedTransform?.LodCount ?? (int)lodTransform.Get("lod_count");
        var lodResolution = typedTransform?.LodDataResolution ?? (int)lodTransform.Get("lod_data_resolution");
        var maxWavelength = typedTransform?.MaxWavelength ?? lodTransform.Get("max_wavelength").AsFloat32Array();
        if (!Mathf.IsEqualApprox(_lastScale, (float)oceanScale) || !_sliceMapBuffer.IsValid)
        {
            _lastScale = (float)oceanScale;
            var map = new int[CrestConstantsCs.CascadeParamsCount];
            for (var lod = 0; lod < lodCount && lod < maxWavelength.Length; lod++)
                map[lod] = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(8.0f * maxWavelength[lod]) /
                    Mathf.Log(2.0f)), 0, CrestFFTComputeCs.CascadeCount - 1);
            if (!_sliceMapBuffer.IsValid)
                _sliceMapBuffer = device.StorageBufferCreate(CrestConstantsCs.CascadeParamsCount * 4u);
            device.BufferUpdate(_sliceMapBuffer, 0, (uint)(map.Length * 4), IntsToBytes(map));
        }

        var cascadeBuffer = FindCascadeBuffer();
        if (!cascadeBuffer.IsValid || maxWavelength.Length == 0) return;
        var wavelength = 0.75f * maxWavelength[0];
        var variance = wavelength > 0.0f
            ? spectrum.chop * spectrum.get_amplitude(wavelength, 8) / wavelength
            : 0.0f;
        _fft.SampleIntoWaveBuffer(waveBuffer, cascadeBuffer, _sliceMapBuffer,
            lodResolution, lodCount, accumulate, weight, variance);
    }

    private Rid FindCascadeBuffer()
    {
        var facadeBuffer = CrestOceanRendererFacade.Instance?.CascadeBufferCurrent ?? new Rid();
        if (facadeBuffer.IsValid) return facadeBuffer;
        for (Node? node = GetParent(); node != null; node = node.GetParent())
        {
            if (!node.HasMethod("cascade_buffer_current")) continue;
            var value = node.Call("cascade_buffer_current");
            if (value.VariantType == Variant.Type.Rid) return value.AsRid();
        }
        return new Rid();
    }

    private void OnSpectrumChanged() => _spectrumDirty = true;
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

    private static byte[] IntsToBytes(int[] values)
    {
        var bytes = new byte[values.Length * sizeof(int)];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
