using Godot;
using System.Collections.Generic;

namespace Crest.Godot;

/// C# scene-facing ocean renderer. It owns public configuration, generated
/// chunks and simulation manager lifecycle while the backend schedules GPU work.
[GlobalClass]
public partial class CrestOceanRendererFacade : Node3D
{
    public static CrestOceanRendererFacade? Instance { get; private set; }
    public float OceanLevel => GlobalPosition.Y;
    public double CurrentTime => (CrestTimeProviderCs.GlobalProvider ?? _runtimeTimeProvider)?.CurrentTime() ?? 0.0;
    public IReadOnlyList<CrestOceanChunkRendererCs> Chunks => _chunks;
    public int SphereDispatchCount => _dynamicWavesManager?.SphereDispatchCount ?? 0;
    public float DynamicWaveDamping => _dynamicWavesManager?.Settings._damping ?? 0.0f;
    private int _lodCountValue = 7;
    private int _lodDataResolutionValue = 384;
    private int _geometryDownSampleFactorValue = 2;
    private float _extentsSizeMultiplierValue = 100.0f;
    private bool _createFoamSimValue = true;
    private bool _createDynamicWaveSimValue;
    private bool _createSeaFloorDepthDataValue = true;
    private bool _createFlowSimValue;
    private bool _createShadowDataValue;
    private bool _createClipSurfaceDataValue;
    private bool _createAlbedoDataValue;
    private bool _rebuildQueued;
    [Export] public int _version { get; set; }
    [Export] public Node3D? _globalWindZone { get; set; }
    [Export(PropertyHint.Range, "0,150,0.1")] public float _globalWindSpeed { get; set; } = 150.0f;
    [Export(PropertyHint.Range, "-180,180,0.1")] public float _globalWindDirectionAngle { get; set; }
    [Export(PropertyHint.Range, "0,1,0.001")] public float _globalWindTurbulence { get; set; } = 0.145f;
    [Export] public Node3D? _viewpoint { get; set; }
    [Export] public Camera3D? _camera { get; set; }
    [Export] public float _teleportThreshold { get; set; } = 10.0f;
    [Export] public Node? _timeProvider { get; set; }
    [Export] public DirectionalLight3D? _primaryLight { get; set; }
    [Export] public bool _searchForPrimaryLightOnStartup { get; set; } = true;
    [Export] public ShaderMaterial? _material { get; set; }
    [Export] public PackedScene? _waterTilePrefab { get; set; }
    [Export] public string _layerName { get; set; } = "";
    [Export] public int _layer { get; set; } = 4;
    [Export] public bool _overrideGravity { get; set; }
    [Export] public float _gravity { get; set; } = -9.8f;
    [Export(PropertyHint.Range, "0,10,0.01")] public float _gravityMultiplier { get; set; } = 1.0f;
    [Export] public bool _waterBodyCulling { get; set; } = true;
    [Export(PropertyHint.Range, "2,15,1")] public int _lodCount
    {
        get => _lodCountValue;
        set { value = Mathf.Clamp(value, 2, CrestConstantsCs.MaxLodCount); if (_lodCountValue != value) { _lodCountValue = value; RequestRebuild(); } }
    }
    [Export] public int _lodDataResolution
    {
        get => _lodDataResolutionValue;
        set { value = Mathf.Max(128, value - value % 128); if (_lodDataResolutionValue != value) { _lodDataResolutionValue = value; RequestRebuild(); } }
    }
    [Export] public int _geometryDownSampleFactor
    {
        get => _geometryDownSampleFactorValue;
        set { value = Mathf.Max(1, value); if (_geometryDownSampleFactorValue != value) { _geometryDownSampleFactorValue = value; RequestRebuild(); } }
    }
    [Export] public float _extentsSizeMultiplier
    {
        get => _extentsSizeMultiplierValue;
        set { if (!Mathf.IsEqualApprox(_extentsSizeMultiplierValue, value)) { _extentsSizeMultiplierValue = value; RequestRebuild(); } }
    }
    [Export] public float _minScale { get; set; } = 8.0f;
    [Export] public float _maxScale { get; set; } = 256.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _dropDetailHeightBasedOnWaves { get; set; } = 0.2f;
    [Export] public bool _createFoamSim
    {
        get => _createFoamSimValue; set { if (_createFoamSimValue != value) { _createFoamSimValue = value; RequestRebuild(); } }
    }
    [Export] public bool _createDynamicWaveSim
    {
        get => _createDynamicWaveSimValue; set { if (_createDynamicWaveSimValue != value) { _createDynamicWaveSimValue = value; RequestRebuild(); } }
    }
    [Export] public bool _createSeaFloorDepthData
    {
        get => _createSeaFloorDepthDataValue; set { if (_createSeaFloorDepthDataValue != value) { _createSeaFloorDepthDataValue = value; RequestRebuild(); } }
    }
    [Export] public bool _createFlowSim
    {
        get => _createFlowSimValue; set { if (_createFlowSimValue != value) { _createFlowSimValue = value; RequestRebuild(); } }
    }
    [Export] public bool _createShadowData
    {
        get => _createShadowDataValue; set { if (_createShadowDataValue != value) { _createShadowDataValue = value; RequestRebuild(); } }
    }
    [Export] public bool _createClipSurfaceData
    {
        get => _createClipSurfaceDataValue; set { if (_createClipSurfaceDataValue != value) { _createClipSurfaceDataValue = value; RequestRebuild(); } }
    }
    [Export] public bool _createAlbedoData
    {
        get => _createAlbedoDataValue; set { if (_createAlbedoDataValue != value) { _createAlbedoDataValue = value; RequestRebuild(); } }
    }
    [Export] public CrestSimSettingsAnimatedWaves? _simSettingsAnimatedWaves { get; set; }
    [Export] public CrestSimSettingsSeaFloorDepth? _simSettingsSeaFloorDepth { get; set; }
    [Export] public CrestSimSettingsFoam? _simSettingsFoam { get; set; }
    [Export] public CrestSimSettingsWave? _simSettingsDynamicWaves { get; set; }
    [Export] public CrestSimSettingsFlow? _simSettingsFlow { get; set; }
    [Export] public CrestSimSettingsShadow? _simSettingsShadow { get; set; }
    [Export] public CrestSimSettingsClipSurface? _simSettingsClipSurface { get; set; }
    [Export] public int _defaultClippingState { get; set; }
    [Export] public CrestSimSettingsAlbedo? _settingsAlbedo { get; set; }
    [Export] public int _surfaceSelfIntersectionFixMode { get; set; } = 2;
    [Export(PropertyHint.Range, "0.000001,0.01,0.000001")] public float _underwaterCullLimit { get; set; } = 0.001f;
    [Export] public bool _fixFlickeringParticleInput { get; set; }
    [Export] public bool _enableRenderQueueSorting { get; set; }
    [Export] public bool _showOceanProxyPlane { get; set; }
    [Export(PropertyHint.Range, "0,60,1")] public float _editModeFPS { get; set; } = 30.0f;
    [Export] public bool _followSceneCamera { get; set; } = true;
    [Export] public bool _heightQueries { get; set; } = true;
    [Export] public CrestOceanDebugFields _debug { get; set; } = new();

    public bool UseCustomTime { get; set; }
    public double CustomTime { get; set; }
    public double TimeScale { get; set; } = 1.0;
    public bool Paused { get; set; }

    private CrestOceanRendererBackend? _backend;
    private CrestTimeProviderCs? _runtimeTimeProvider;
    private CrestFoamSimulationManagerCs? _foamManager;
    private CrestSeaFloorDepthManagerCs? _depthManager;
    private CrestFlowManagerCs? _flowManager;
    private CrestClipSurfaceManagerCs? _clipManager;
    private CrestAlbedoManagerCs? _albedoManager;
    private CrestShadowManagerCs? _shadowManager;
    private CrestAnimatedWavesManagerCs? _animatedWavesManager;
    private CrestDynamicWavesManagerCs? _dynamicWavesManager;
    private readonly List<CrestOceanChunkRendererCs> _chunks = new();

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        ProcessPriority = 100;
        BuildFacade();
    }

    private void BuildFacade()
    {
        var enableFlow = _createFlowSim || OS.GetEnvironment("CREST_FORCE_FLOW") == "1";
        var enableClip = _createClipSurfaceData || OS.GetEnvironment("CREST_FORCE_CLIP") == "1";
        var enableAlbedo = _createAlbedoData || OS.GetEnvironment("CREST_FORCE_ALBEDO") == "1";
        var enableShadow = _createShadowData || OS.GetEnvironment("CREST_FORCE_SHADOW") == "1";
        _runtimeTimeProvider = new CrestTimeProviderCs
        {
            UseCustomTime = UseCustomTime,
            CustomTime = CustomTime,
            TimeScale = TimeScale,
            Paused = Paused,
        };
        var scene = GD.Load<PackedScene>("res://addons/crest/core/ocean_backend.tscn");
        if (scene == null)
        {
            GD.PushError("CrestOceanRendererFacade: backend script is missing.");
            return;
        }
        _backend = scene.Instantiate<CrestOceanRendererBackend>();
        _backend.LodCount = _lodCount;
        _backend.LodDataResolution = _lodDataResolution;
        _backend.GeometryDownSampleFactor = _geometryDownSampleFactor;
        _backend.MinScale = _minScale;
        _backend.MaxScale = _maxScale;
        _backend.Gravity = _gravityMultiplier * Mathf.Abs(_overrideGravity ? _gravity : CrestConstantsCs.Gravity);
        _backend.DropDetailHeightBasedOnWaves = _dropDetailHeightBasedOnWaves > 0.0f;
        _backend.CreateFoamSim = _createFoamSim;
        _backend.CreateDynamicWaveSim = _createDynamicWaveSim;
        _backend.CreateSeaFloorDepthData = _createSeaFloorDepthData;
        _backend.CreateFlowSim = enableFlow;
        _backend.CreateShadowSim = enableShadow;
        _backend.CreateClipSurfaceData = enableClip;
        _backend.CreateAlbedoData = enableAlbedo;
        _backend.Viewpoint = _viewpoint;
        _backend.SimSettingsWave = _simSettingsDynamicWaves;
        _backend.SimSettingsFoam = _simSettingsFoam;
        _backend.SimSettingsFlow = _simSettingsFlow;
        _backend.SimSettingsShadow = _simSettingsShadow;
        _backend.SimSettingsClipSurface = _simSettingsClipSurface;
        _backend.SimSettingsAlbedo = _settingsAlbedo;
        if (_material == null)
        {
            var shader = GD.Load<Shader>("res://addons/crest/shaders/ocean.gdshader");
            if (shader == null)
            {
                GD.PushError("CrestOceanRendererFacade: ocean.gdshader not found.");
                return;
            }
            _material = new ShaderMaterial { Shader = shader };
        }
        _backend.OceanMaterial = _material;
        if (_createFoamSim)
        {
            _foamManager = new CrestFoamSimulationManagerCs();
            _backend.Foam = _foamManager;
        }
        _animatedWavesManager = new CrestAnimatedWavesManagerCs();
        _backend.AnimatedWaves = _animatedWavesManager;
        if (_createDynamicWaveSim)
        {
            _dynamicWavesManager = new CrestDynamicWavesManagerCs();
            _backend.DynamicWaves = _dynamicWavesManager;
        }
        if (_createSeaFloorDepthData)
        {
            _depthManager = new CrestSeaFloorDepthManagerCs();
            _backend.Depth = _depthManager;
        }
        if (enableFlow)
        {
            _flowManager = new CrestFlowManagerCs();
            _backend.Flow = _flowManager;
        }
        if (enableClip)
        {
            _clipManager = new CrestClipSurfaceManagerCs();
            _backend.ClipSurface = _clipManager;
        }
        if (enableAlbedo)
        {
            _albedoManager = new CrestAlbedoManagerCs();
            _backend.Albedo = _albedoManager;
        }
        if (enableShadow)
        {
            _shadowManager = new CrestShadowManagerCs();
            _backend.Shadow = _shadowManager;
        }

        var tileResolution = CrestOceanBuilderCs.GetTileResolution(_lodDataResolution, _geometryDownSampleFactor);
        var extents = _extentsSizeMultiplier * (CrestConstantsCs.MaxLodCount + 1 - _lodCount);
        var meshesArray = CrestOceanBuilderCs.BuildPatchMeshes(tileResolution, extents);
        var generatedMeshes = new global::Godot.Collections.Array();
        foreach (var mesh in meshesArray)
            generatedMeshes.Add(mesh);
        _backend.PatchMeshes = generatedMeshes;

        if (_material != null)
        {
            var tilesRoot = new Node3D { Name = "CrestTiles" };
            var generatedChunks = new global::Godot.Collections.Array();
            for (var lod = 0; lod < _lodCount; lod++)
            {
                foreach (var chunk in CrestOceanBuilderCs.CreateLodChunks(tilesRoot, lod, _lodCount,
                    meshesArray, _material, extents))
                {
                    chunk.ExpandBounds(30.0f, 30.0f);
                    _chunks.Add(chunk);
                    generatedChunks.Add(chunk);
                }
            }
            _backend.TilesRoot = tilesRoot;
            _backend.Chunks = generatedChunks;
        }
        AddChild(_backend, false, Node.InternalMode.Front);

        // The facade owns the frame callback while the backend is being
        // ported. Its _ready callback has already built the GPU resources.
        _backend.SetProcess(false);

        // Keep wave generators and registered inputs under the backend so its
        // existing descendant collection sees exactly the same hierarchy.
        foreach (var child in GetChildren())
        {
            if (child != _backend)
                child.Reparent(_backend);
        }
    }

    public override void _Process(double delta)
    {
        CrestRDComputeCs.FlushDeferredFrees();
        if (_rebuildQueued)
        {
            _rebuildQueued = false;
            RebuildFacade();
        }
        if (_backend != null)
        {
            _runtimeTimeProvider!.UseCustomTime = UseCustomTime;
            _runtimeTimeProvider.CustomTime = CustomTime;
            _runtimeTimeProvider.TimeScale = TimeScale;
            _runtimeTimeProvider.Paused = Paused;
            var provider = CrestTimeProviderCs.GlobalProvider ?? _runtimeTimeProvider;
            provider.Advance(delta);
            _backend.ExternalTime = provider.CurrentTime();
            _backend.RunFrame(delta);
        }
    }

    public float GetOceanScale() => _backend?.OceanScale ?? _minScale;
    public float GetViewerAltitudeLevelAlpha() => _backend?.ViewerAltitudeLevelAlpha ?? 0.0f;
    public float GetViewerScaleTransitionBlend() => _backend?.ViewerScaleTransitionBlend ?? 0.0f;
    public double GetCurrentTime() => CurrentTime;
    public Rid CascadeBufferCurrent => _backend?.CascadeBufferCurrent ?? new Rid();
    public Rid CascadeBufferSource => _backend?.CascadeBufferSource ?? new Rid();

    public void SetPlanarReflection(Texture2D? texture, float intensity)
    {
        if (_backend == null) return;
        _backend.ExternalPlanarReflection = texture;
        _backend.ExternalPlanarReflectionIntensity = texture != null ? intensity : 0.0f;
    }

    public void InvalidateDepthTexture(Texture2D? texture) =>
        _depthManager?.InvalidateTexture(texture);

    public IReadOnlyList<(string Label, Texture2DArrayRD Texture)> GetDebugTextures()
    {
        var entries = new List<(string, Texture2DArrayRD)>();
        if (_animatedWavesManager != null) entries.Add(("anim waves", _animatedWavesManager.texture_array));
        if (_foamManager != null) entries.Add(("foam", _foamManager.texture_array));
        if (_dynamicWavesManager != null) entries.Add(("dyn waves", _dynamicWavesManager.texture_array));
        if (_depthManager != null) entries.Add(("sea depth", _depthManager.texture_array));
        if (_flowManager != null) entries.Add(("flow", _flowManager.texture_array));
        if (_shadowManager != null) entries.Add(("shadow", _shadowManager.texture_array));
        if (_clipManager != null) entries.Add(("clip", _clipManager.texture_array));
        if (_albedoManager != null) entries.Add(("albedo", _albedoManager.texture_array));
        return entries;
    }

    private void RequestRebuild()
    {
        if (_backend != null && IsInsideTree()) _rebuildQueued = true;
    }

    private void RebuildFacade()
    {
        if (_backend != null)
        {
            foreach (var child in _backend.GetChildren())
                if (child != _backend.TilesRoot)
                    child.Reparent(this);
            _backend.DestroyOcean();
            _backend.Free();
        }
        ClearRuntimeReferences();
        BuildFacade();
    }

    private void ClearRuntimeReferences()
    {
        _backend = null;
        _foamManager = null;
        _depthManager = null;
        _flowManager = null;
        _clipManager = null;
        _albedoManager = null;
        _shadowManager = null;
        _animatedWavesManager = null;
        _dynamicWavesManager = null;
        _chunks.Clear();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        _backend?.DestroyOcean();
        for (var i = 0; i < 4; i++)
            CrestRDComputeCs.FlushDeferredFrees();
        _backend?.QueueFree();
        ClearRuntimeReferences();

        // Godot 4.6 can otherwise run Variant finalizers after the native
        // servers have begun teardown, causing a shutdown-only access fault.
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
    }
}

[GlobalClass]
public partial class CrestOceanDebugFields : Resource
{
    [Export] public bool _attachDebugGUI { get; set; }
    [Export] public bool _showOceanTileGameObjects { get; set; }
    [Export] public bool _disableFollowViewpoint { get; set; }
    [Export] public bool _destroyResourcesInOnDisable { get; set; }
    [Export] public bool _forceBatchMode { get; set; }
    [Export] public bool _forceNoGPU { get; set; }
}
