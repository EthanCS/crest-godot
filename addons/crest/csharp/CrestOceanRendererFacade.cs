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
    public double CurrentTime => (CrestTimeProviderCs.GlobalProvider ?? _timeProvider)?.CurrentTime() ?? 0.0;
    public IReadOnlyList<CrestOceanChunkRendererCs> Chunks => _chunks;
    public int SphereDispatchCount => _dynamicWavesManager?.SphereDispatchCount ?? 0;
    public float DynamicWaveDamping => _dynamicWavesManager?.Settings.damping ?? 0.0f;
    private int _lodCount = 7;
    private int _lodDataResolution = 256;
    private int _geometryDownSampleFactor = 2;
    private float _extentsSizeMultiplier = 100.0f;
    private bool _createFoamSim = true;
    private bool _createDynamicWaveSim = true;
    private bool _createSeaFloorDepthData = true;
    private bool _createFlowSim;
    private bool _createShadowSim;
    private bool _createClipSurfaceData;
    private bool _createAlbedoData;
    private bool _rebuildQueued;
    [Export(PropertyHint.Range, "2,15,1")] public int lod_count
    {
        get => _lodCount;
        set { value = Mathf.Clamp(value, 2, CrestConstantsCs.MaxLodCount); if (_lodCount != value) { _lodCount = value; RequestRebuild(); } }
    }
    [Export] public int lod_data_resolution
    {
        get => _lodDataResolution;
        set { value = Mathf.Max(16, value - value % 16); if (_lodDataResolution != value) { _lodDataResolution = value; RequestRebuild(); } }
    }
    [Export] public int geometry_down_sample_factor
    {
        get => _geometryDownSampleFactor;
        set { value = Mathf.Max(1, value); if (_geometryDownSampleFactor != value) { _geometryDownSampleFactor = value; RequestRebuild(); } }
    }
    [Export] public float extents_size_multiplier
    {
        get => _extentsSizeMultiplier;
        set { if (!Mathf.IsEqualApprox(_extentsSizeMultiplier, value)) { _extentsSizeMultiplier = value; RequestRebuild(); } }
    }
    [Export] public float min_scale { get; set; } = 8.0f;
    [Export] public float max_scale { get; set; } = 256.0f;
    [Export] public float gravity { get; set; } = 9.81f;
    [Export] public bool drop_detail_height_based_on_waves { get; set; } = true;
    [Export] public bool create_foam_sim
    {
        get => _createFoamSim; set { if (_createFoamSim != value) { _createFoamSim = value; RequestRebuild(); } }
    }
    [Export] public bool create_dynamic_wave_sim
    {
        get => _createDynamicWaveSim; set { if (_createDynamicWaveSim != value) { _createDynamicWaveSim = value; RequestRebuild(); } }
    }
    [Export] public bool create_sea_floor_depth_data
    {
        get => _createSeaFloorDepthData; set { if (_createSeaFloorDepthData != value) { _createSeaFloorDepthData = value; RequestRebuild(); } }
    }
    [Export] public bool create_flow_sim
    {
        get => _createFlowSim; set { if (_createFlowSim != value) { _createFlowSim = value; RequestRebuild(); } }
    }
    [Export] public bool create_shadow_sim
    {
        get => _createShadowSim; set { if (_createShadowSim != value) { _createShadowSim = value; RequestRebuild(); } }
    }
    [Export] public bool create_clip_surface_data
    {
        get => _createClipSurfaceData; set { if (_createClipSurfaceData != value) { _createClipSurfaceData = value; RequestRebuild(); } }
    }
    [Export] public bool create_albedo_data
    {
        get => _createAlbedoData; set { if (_createAlbedoData != value) { _createAlbedoData = value; RequestRebuild(); } }
    }
    [Export] public Resource? sim_settings_wave { get; set; }
    [Export] public CrestSimSettingsFoam? sim_settings_foam { get; set; }
    [Export] public CrestSimSettingsFlow? sim_settings_flow { get; set; }
    [Export] public CrestSimSettingsShadow? sim_settings_shadow { get; set; }
    [Export] public CrestSimSettingsClipSurface? sim_settings_clip_surface { get; set; }
    [Export] public CrestSimSettingsAlbedo? sim_settings_albedo { get; set; }
    [Export] public ShaderMaterial? ocean_material { get; set; }
    [Export] public Node3D? viewpoint { get; set; }
    [Export] public bool use_custom_time { get; set; }
    [Export] public double custom_time { get; set; }
    [Export] public double time_scale { get; set; } = 1.0;
    [Export] public bool paused { get; set; }

    private CrestOceanRendererBackend? _backend;
    private CrestTimeProviderCs? _timeProvider;
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
        var enableFlow = create_flow_sim || OS.GetEnvironment("CREST_FORCE_FLOW") == "1";
        var enableClip = create_clip_surface_data || OS.GetEnvironment("CREST_FORCE_CLIP") == "1";
        var enableAlbedo = create_albedo_data || OS.GetEnvironment("CREST_FORCE_ALBEDO") == "1";
        var enableShadow = create_shadow_sim || OS.GetEnvironment("CREST_FORCE_SHADOW") == "1";
        _timeProvider = new CrestTimeProviderCs
        {
            UseCustomTime = use_custom_time,
            CustomTime = custom_time,
            TimeScale = time_scale,
            Paused = paused,
        };
        var scene = GD.Load<PackedScene>("res://addons/crest/core/ocean_backend.tscn");
        if (scene == null)
        {
            GD.PushError("CrestOceanRendererFacade: backend script is missing.");
            return;
        }
        _backend = scene.Instantiate<CrestOceanRendererBackend>();
        _backend.LodCount = lod_count;
        _backend.LodDataResolution = lod_data_resolution;
        _backend.GeometryDownSampleFactor = geometry_down_sample_factor;
        _backend.MinScale = min_scale;
        _backend.MaxScale = max_scale;
        _backend.Gravity = gravity;
        _backend.DropDetailHeightBasedOnWaves = drop_detail_height_based_on_waves;
        _backend.CreateFoamSim = create_foam_sim;
        _backend.CreateDynamicWaveSim = create_dynamic_wave_sim;
        _backend.CreateSeaFloorDepthData = create_sea_floor_depth_data;
        _backend.CreateFlowSim = enableFlow;
        _backend.CreateShadowSim = enableShadow;
        _backend.CreateClipSurfaceData = enableClip;
        _backend.CreateAlbedoData = enableAlbedo;
        _backend.Viewpoint = viewpoint;
        _backend.SimSettingsWave = sim_settings_wave;
        _backend.SimSettingsFoam = sim_settings_foam;
        _backend.SimSettingsFlow = sim_settings_flow;
        _backend.SimSettingsShadow = sim_settings_shadow;
        _backend.SimSettingsClipSurface = sim_settings_clip_surface;
        _backend.SimSettingsAlbedo = sim_settings_albedo;
        if (ocean_material == null)
        {
            var shader = GD.Load<Shader>("res://addons/crest/shaders/ocean.gdshader");
            if (shader == null)
            {
                GD.PushError("CrestOceanRendererFacade: ocean.gdshader not found.");
                return;
            }
            ocean_material = new ShaderMaterial { Shader = shader };
        }
        _backend.OceanMaterial = ocean_material;
        if (create_foam_sim)
        {
            _foamManager = new CrestFoamSimulationManagerCs();
            _backend.Foam = _foamManager;
        }
        _animatedWavesManager = new CrestAnimatedWavesManagerCs();
        _backend.AnimatedWaves = _animatedWavesManager;
        if (create_dynamic_wave_sim)
        {
            _dynamicWavesManager = new CrestDynamicWavesManagerCs();
            _backend.DynamicWaves = _dynamicWavesManager;
        }
        if (create_sea_floor_depth_data)
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

        var tileResolution = CrestOceanBuilderCs.GetTileResolution(lod_data_resolution, geometry_down_sample_factor);
        var extents = extents_size_multiplier * (CrestConstantsCs.MaxLodCount + 1 - lod_count);
        var meshesArray = CrestOceanBuilderCs.BuildPatchMeshes(tileResolution, extents);
        var generatedMeshes = new global::Godot.Collections.Array();
        foreach (var mesh in meshesArray)
            generatedMeshes.Add(mesh);
        _backend.PatchMeshes = generatedMeshes;

        if (ocean_material != null)
        {
            var tilesRoot = new Node3D { Name = "CrestTiles" };
            var generatedChunks = new global::Godot.Collections.Array();
            for (var lod = 0; lod < lod_count; lod++)
            {
                foreach (var chunk in CrestOceanBuilderCs.CreateLodChunks(tilesRoot, lod, lod_count,
                    meshesArray, ocean_material, extents))
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
            _timeProvider!.UseCustomTime = use_custom_time;
            _timeProvider.CustomTime = custom_time;
            _timeProvider.TimeScale = time_scale;
            _timeProvider.Paused = paused;
            var provider = CrestTimeProviderCs.GlobalProvider ?? _timeProvider;
            provider.Advance(delta);
            _backend.ExternalTime = provider.CurrentTime();
            _backend.RunFrame(delta);
        }
    }

    public float get_ocean_scale() => _backend?.OceanScale ?? min_scale;
    public float get_viewer_altitude_level_alpha() => _backend?.ViewerAltitudeLevelAlpha ?? 0.0f;
    public double get_current_time() => CurrentTime;
    public Rid CascadeBufferCurrent => _backend?.CascadeBufferCurrent ?? new Rid();
    public Rid CascadeBufferSource => _backend?.CascadeBufferSource ?? new Rid();
    public Rid cascade_buffer_current() => CascadeBufferCurrent;
    public Rid cascade_buffer_source() => CascadeBufferSource;

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
