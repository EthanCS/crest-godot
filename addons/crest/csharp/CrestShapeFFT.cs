using Godot;

namespace Crest.Godot;

/// FFT wave generator that maps the 16 tiled spectral bands into the
/// view-following animated-wave cascades.
[Tool]
public partial class CrestShapeFFT : Node3D, ICrestShapeGenerator
{
    private CrestWaveSpectrum? _spectrumValue;
    private int _resolutionValue = 128;
    private CrestFFTComputeCs? _fft;
    private float _windTurbulenceValue = 0.145f;
    private bool _spectrumDirty = true;
    private bool _reinitialize = true;
    private Rid _sliceMapBuffer;
    private float _lastScale = -1.0f;
    public int SampleDispatchCount => _fft?.SampleDispatchCount ?? 0;

    [Export]
    public CrestWaveSpectrum? _spectrum
    {
        get => _spectrumValue;
        set
        {
            if (_spectrumValue == value) return;
            DisconnectSpectrum(_spectrumValue);
            _spectrumValue = value;
            ConnectSpectrum(_spectrumValue);
            _spectrumDirty = true;
        }
    }

    [Export(PropertyHint.Range, "16,512,1")]
    public int _resolution
    {
        get => _resolutionValue;
        set
        {
            var exponent = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(Mathf.Max(value, 16)) /
                Mathf.Log(2.0f)), 4, 9);
            var clamped = 1 << exponent;
            if (_resolutionValue == clamped) return;
            _resolutionValue = clamped;
            _reinitialize = true;
            _spectrumDirty = true;
        }
    }

    [Export] public bool _spectrumFixedAtRuntime { get; set; } = true;
    [Export] public bool _overrideGlobalWindDirection { get; set; }
    [Export(PropertyHint.Range, "-180,180,0.1")] public float _waveDirectionHeadingAngle { get; set; }
    [Export] public bool _overrideGlobalWindSpeed { get; set; }
    [Export(PropertyHint.Range, "0,150,0.1")] public float _windSpeed { get; set; } = 20.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _respectShallowWaterAttenuation { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _weight { get; set; } = 1.0f;
    [Export] public int _blendMode { get; set; }
    [Export] public int _queue { get; set; }
    [Export] public float _featherWidth { get; set; }
    [Export] public bool _overrideSplineSettings { get; set; }
    [Export] public float _radius { get; set; } = 50.0f;
    [Export] public int _subdivisions { get; set; } = 1;
    [Export] public float _featherWaveStart { get; set; } = 0.1f;
    [Export] public int _version { get; set; } = 1;
    [Export] public bool _overrideGlobalWindTurbulence { get; set; }
    [Export(PropertyHint.Range, "0,1,0.001")]
    public float _windTurbulence
    {
        get => _windTurbulenceValue;
        set
        {
            var clamped = Mathf.Clamp(value, 0.0f, 1.0f);
            if (Mathf.IsEqualApprox(_windTurbulenceValue, clamped)) return;
            _windTurbulenceValue = clamped;
            _spectrumDirty = true;
        }
    }

    [Export] public float _maxVerticalDisplacement { get; set; } = 10.0f;
    [Export] public float _maxHorizontalDisplacement { get; set; } = 15.0f;
    [Export] public bool _enableBakedCollision { get; set; }
    [Export] public int _timeResolution { get; set; } = 4;
    [Export] public float _smallestWavelengthRequired { get; set; } = 2.0f;
    [Export(PropertyHint.Range, "4,128,1")] public float _timeLoopLength { get; set; } = 32.0f;
    [Export] public CrestShapeDebugFields _debug { get; set; } = new();

    public override void _EnterTree()
    {
        ConnectSpectrum(_spectrumValue);
        AddToGroup("crest_shape_generator");
        AddToGroup("crest_shape_generator_cs");
    }

    public override void _ExitTree()
    {
        DisconnectSpectrum(_spectrumValue);
        _fft?.FreeRids();
        _fft = null;
        var device = RenderingServer.GetRenderingDevice();
        if (device != null && _sliceMapBuffer.IsValid)
            CrestRDComputeCs.FreeRidDeferred(device, _sliceMapBuffer);
        _sliceMapBuffer = new Rid();
    }

    public void Evaluate(Rid waveBuffer, CrestSeaFloorDepthManagerCs? depthManager, CrestLodTransformCs lodTransform,
        double oceanScale, double oceanLevel, double time, bool accumulate = false)
    {
        _ = depthManager;
        _ = oceanLevel;
        var device = RenderingServer.GetRenderingDevice();
        if (device == null) return;
        _spectrum ??= new CrestWaveSpectrum();

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
            var angle = Mathf.DegToRad(WindDirectionAngle());
            _fft.RebuildSpectrum(_spectrum, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)),
                WindSpeedMetersPerSecond(), WindTurbulence(), 0.0f);
            _spectrumDirty = false;
        }

        _fft.AdvanceTime((float)time * _spectrum._gravityScale, _spectrum._gravityScale, _spectrum._chop);
        var lodCount = lodTransform.LodCount;
        var lodResolution = lodTransform.LodDataResolution;
        var maxWavelength = lodTransform.MaxWavelength;
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
            ? _spectrum._chop * _spectrum.get_amplitude(wavelength, 8, WindSpeedMetersPerSecond()) / wavelength
            : 0.0f;
        _fft.SampleIntoWaveBuffer(waveBuffer, cascadeBuffer, _sliceMapBuffer,
            lodResolution, lodCount, accumulate, _weight, variance);
    }

    private float WindDirectionAngle() => _overrideGlobalWindDirection
        ? _waveDirectionHeadingAngle
        : CrestOceanRendererFacade.Instance?._globalWindDirectionAngle ?? _waveDirectionHeadingAngle;

    private float WindSpeedMetersPerSecond() => (_overrideGlobalWindSpeed
        ? _windSpeed
        : CrestOceanRendererFacade.Instance?._globalWindSpeed ?? _windSpeed) / 3.6f;

    private float WindTurbulence() => _overrideGlobalWindTurbulence
        ? _windTurbulence
        : CrestOceanRendererFacade.Instance?._globalWindTurbulence ?? _windTurbulence;

    private Rid FindCascadeBuffer()
    {
        var facadeBuffer = CrestOceanRendererFacade.Instance?.CascadeBufferCurrent ?? new Rid();
        if (facadeBuffer.IsValid) return facadeBuffer;
        for (Node? node = GetParent(); node != null; node = node.GetParent())
        {
            if (node is CrestOceanRendererBackend backend) return backend.CascadeBufferCurrent;
            if (node is CrestOceanRendererFacade facade) return facade.CascadeBufferCurrent;
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

[GlobalClass]
public partial class CrestShapeDebugFields : Resource
{
    [Export] public bool _drawBounds { get; set; }
    [Export] public bool _drawSlicesInEditor { get; set; }
}
