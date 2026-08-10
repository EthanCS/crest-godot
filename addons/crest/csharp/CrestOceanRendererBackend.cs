using Godot;
using Godot.Collections;
using System.Globalization;

namespace Crest.Godot;

/// C# ocean runtime backend. The facade supplies meshes, managers and time;
/// this node owns cascade buffers, simulation scheduling and material sync.
public partial class CrestOceanRendererBackend : Node3D
{
    public int LodCount { get; set; } = CrestConstantsCs.DefaultLodCount;
    public int LodDataResolution { get; set; } = CrestConstantsCs.DefaultLodDataResolution;
    public int GeometryDownSampleFactor { get; set; } = 2;
    public float MinScale { get; set; } = 8.0f;
    public float MaxScale { get; set; } = 256.0f;
    public float Gravity { get; set; } = CrestConstantsCs.Gravity;
    public bool DropDetailHeightBasedOnWaves { get; set; } = true;
    public bool CreateFoamSim { get; set; } = true;
    public bool CreateDynamicWaveSim { get; set; } = true;
    public bool CreateSeaFloorDepthData { get; set; } = true;
    public bool CreateFlowSim { get; set; }
    public bool CreateShadowSim { get; set; }
    public bool CreateClipSurfaceData { get; set; }
    public bool CreateAlbedoData { get; set; }
    public Resource? SimSettingsWave { get; set; }
    public CrestSimSettingsFoam? SimSettingsFoam { get; set; }
    public CrestSimSettingsFlow? SimSettingsFlow { get; set; }
    public CrestSimSettingsShadow? SimSettingsShadow { get; set; }
    public CrestSimSettingsClipSurface? SimSettingsClipSurface { get; set; }
    public CrestSimSettingsAlbedo? SimSettingsAlbedo { get; set; }
    public ShaderMaterial? OceanMaterial { get; set; }
    public Node3D? Viewpoint { get; set; }
    public double ExternalTime { get; set; }
    public Texture2D? ExternalPlanarReflection { get; set; }
    public float ExternalPlanarReflectionIntensity { get; set; }
    public Array PatchMeshes { get; set; } = new();
    public Node3D? TilesRoot { get; set; }
    public Array Chunks { get; set; } = new();

    public CrestAnimatedWavesManagerCs? AnimatedWaves { get; set; }
    public CrestFoamSimulationManagerCs? Foam { get; set; }
    public CrestDynamicWavesManagerCs? DynamicWaves { get; set; }
    public CrestFlowManagerCs? Flow { get; set; }
    public CrestSeaFloorDepthManagerCs? Depth { get; set; }
    public CrestShadowManagerCs? Shadow { get; set; }
    public CrestClipSurfaceManagerCs? ClipSurface { get; set; }
    public CrestAlbedoManagerCs? Albedo { get; set; }

    public float OceanScale { get; private set; } = 8.0f;
    public float ViewerAltitudeLevelAlpha { get; private set; }
    public float ViewerHeightAboveWater { get; private set; }
    public float OceanLevel { get; private set; }
    public Rid CascadeBufferCurrent => _cascadeCurrent;
    public Rid CascadeBufferSource => _cascadeSource;
    public Rid cascade_buffer_current() => _cascadeCurrent;
    public Rid cascade_buffer_source() => _cascadeSource;

    private CrestLodTransformCs? _lodTransform;
    private RenderingDevice? _device;
    private Rid _cascadeCurrent;
    private Rid _cascadeSource;
    private float _lodChange;
    private bool _built;
    private Texture2DArrayRD? _fallbackTextureArray;
    private Rid _fallbackTextureArrayRid;
    private Texture2D? _fallbackTexture2D;

    public override void _Ready() => BuildOcean();

    public override void _ExitTree() => DestroyOcean();

    public void RunFrame(double delta)
    {
        if (!_built || _lodTransform == null || TilesRoot == null || AnimatedWaves == null) return;
        CrestRDComputeCs.FlushDeferredFrees();
        var viewer = GetViewerPosition();
        var rootXz = new Vector2(viewer.X, viewer.Z);
        if (Mathf.Abs(Mathf.PosMod(rootXz.X * 60.0f, 1.0f)) < 0.003f) rootXz.X += 0.002f;
        if (Mathf.Abs(Mathf.PosMod(rootXz.Y * 60.0f, 1.0f)) < 0.003f) rootXz.Y += 0.002f;
        TilesRoot.GlobalPosition = new Vector3(rootXz.X, OceanLevel, rootXz.Y);

        UpdateViewerHeight(viewer);
        UpdateScale();
        TilesRoot.Scale = new Vector3(OceanScale, 1.0f, OceanScale);
        _lodTransform.UpdateTransforms(OceanScale, rootXz);
        UploadCascadeData();

        Depth?.update_sim(_lodTransform, _cascadeCurrent, CollectInputs("crest_depth_input"));
        Flow?.update_sim(_lodTransform, _cascadeCurrent, CollectInputs("crest_flow_input"));
        DynamicWaves?.update_sim(delta, _lodTransform, _cascadeCurrent, _cascadeSource,
            Flow, Depth, OceanLevel, Gravity, _lodChange, CollectDictionaryCalls("crest_sphere_interaction", "get_sphere_injection"));
        AnimatedWaves.update(CollectObjects("crest_shape_generator", "evaluate"), Depth, DynamicWaves,
            _lodTransform, _cascadeCurrent, OceanScale, OceanLevel, ExternalTime,
            DynamicWaves != null ? SimSettingsWave : null, CollectInputs("crest_anim_waves_input"));
        if (Foam != null)
        {
            Foam.update_sim(delta, _lodTransform, _cascadeCurrent, _cascadeSource,
                Flow, AnimatedWaves, Depth, OceanLevel, _lodChange);
            Foam.inject_inputs(_lodTransform, _cascadeCurrent, CollectInputs("crest_foam_input"), ExternalTime);
        }
        if (Shadow != null)
        {
            var light = FindDirectionalLight();
            if (light != null) Shadow.light_dir = -light.GlobalTransform.Basis.Z.Normalized();
            Shadow.update_sim(delta, _lodTransform, _cascadeCurrent, _cascadeSource,
                AnimatedWaves, OceanLevel, _lodChange, ExternalTime,
                CollectDictionaryCalls("crest_shadow_input", "get_shadow_caster"));
        }
        ClipSurface?.update_sim(_lodTransform, _cascadeCurrent, CollectInputs("crest_clip_input"));
        Albedo?.update_sim(_lodTransform, _cascadeCurrent, CollectInputs("crest_albedo_input"));
        SyncMaterialParams();
    }

    private void BuildOcean()
    {
        if (_built) return;
        _device = RenderingServer.GetRenderingDevice();
        if (_device == null)
        {
            GD.PushWarning("CrestOceanRenderer: no RenderingDevice. Ocean disabled.");
            return;
        }
        OceanLevel = GlobalPosition.Y;
        OceanScale = MinScale;
        _lodTransform = new CrestLodTransformCs(LodCount, LodDataResolution);
        var cascadeBytes = (uint)(CrestConstantsCs.CascadeParamsCount * 8 * sizeof(float));
        _cascadeCurrent = _device.StorageBufferCreate(cascadeBytes);
        _cascadeSource = _device.StorageBufferCreate(cascadeBytes);

        AnimatedWaves ??= new CrestAnimatedWavesManagerCs();
        AnimatedWaves.init_mgr(LodDataResolution, LodCount);
        if (CreateFoamSim) (Foam ??= new CrestFoamSimulationManagerCs()).init_mgr(LodDataResolution, LodCount, SimSettingsFoam);
        if (CreateDynamicWaveSim) (DynamicWaves ??= new CrestDynamicWavesManagerCs()).init_mgr(LodDataResolution, LodCount, SimSettingsWave);
        if (CreateSeaFloorDepthData) (Depth ??= new CrestSeaFloorDepthManagerCs()).init_mgr(LodDataResolution, LodCount);
        if (CreateFlowSim) (Flow ??= new CrestFlowManagerCs()).init_mgr(LodDataResolution, LodCount, SimSettingsFlow);
        if (CreateShadowSim) (Shadow ??= new CrestShadowManagerCs()).init_mgr(LodDataResolution, LodCount, SimSettingsShadow);
        if (CreateClipSurfaceData) (ClipSurface ??= new CrestClipSurfaceManagerCs()).init_mgr(LodDataResolution, LodCount, SimSettingsClipSurface);
        if (CreateAlbedoData) (Albedo ??= new CrestAlbedoManagerCs()).init_mgr(LodDataResolution, LodCount, SimSettingsAlbedo);

        _lodTransform.UpdateTransforms(OceanScale, new Vector2(GlobalPosition.X, GlobalPosition.Z));
        UploadCascadeData();
        BuildTiles();
        _built = true;
    }

    private void BuildTiles()
    {
        if (OceanMaterial == null)
        {
            var shader = GD.Load<Shader>("res://addons/crest/shaders/ocean.gdshader");
            if (shader == null) { GD.PushError("CrestOceanRenderer: ocean.gdshader not found."); return; }
            OceanMaterial = new ShaderMaterial { Shader = shader };
        }
        TilesRoot ??= new Node3D { Name = "CrestTiles" };
        if (TilesRoot.GetParent() != this) AddChild(TilesRoot);
        SyncStaticMaterialParams();
    }

    public void DestroyOcean()
    {
        if (!_built && !_cascadeCurrent.IsValid && !_cascadeSource.IsValid) return;
        _built = false;
        if (TilesRoot != null && IsInstanceValid(TilesRoot)) TilesRoot.QueueFree();
        TilesRoot = null;
        Chunks.Clear();
        AnimatedWaves?.free_rids(); Foam?.free_rids(); DynamicWaves?.free_rids();
        Flow?.free_rids(); Depth?.free_rids(); Shadow?.free_rids(); ClipSurface?.free_rids(); Albedo?.free_rids();
        if (_device != null)
        {
            if (_cascadeCurrent.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, _cascadeCurrent);
            if (_cascadeSource.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, _cascadeSource);
            if (_fallbackTextureArrayRid.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, _fallbackTextureArrayRid);
        }
        _cascadeCurrent = new Rid(); _cascadeSource = new Rid(); _fallbackTextureArrayRid = new Rid();
        if (_fallbackTextureArray != null) _fallbackTextureArray.TextureRdRid = new Rid();
        _fallbackTextureArray = null; _fallbackTexture2D = null; _lodTransform = null;
    }

    private void UpdateViewerHeight(Vector3 viewer)
    {
        var height = viewer.Y - OceanLevel;
        if (Mathf.Abs(height - ViewerHeightAboveWater) > 10.0f) Foam?.NotifyTeleport();
        ViewerHeightAboveWater = Mathf.Lerp(ViewerHeightAboveWater, height, 0.05f);
    }

    private void UpdateScale()
    {
        var height = ViewerHeightAboveWater + (DropDetailHeightBasedOnWaves ? 0.2f : 0.0f);
        var distance = Mathf.Max(Mathf.Abs(height) - 4.0f, 0.0f);
        var level = Mathf.Clamp(distance, MinScale, 1.99f * MaxScale);
        var log2 = Mathf.Log(level) / Mathf.Log(2.0f);
        var floor = Mathf.Floor(log2);
        ViewerAltitudeLevelAlpha = log2 - floor;
        var newScale = Mathf.Pow(2.0f, floor);
        _lodChange = Mathf.IsEqualApprox(newScale, OceanScale) ? 0.0f :
            Mathf.Round(Mathf.Log(newScale / OceanScale) / Mathf.Log(2.0f));
        OceanScale = newScale;
    }

    private Vector3 GetViewerPosition()
    {
        if (Viewpoint != null) return Viewpoint.GlobalPosition;
        return GetViewport()?.GetCamera3D()?.GlobalPosition ?? GlobalPosition;
    }

    private void UploadCascadeData()
    {
        if (_device == null || _lodTransform == null || !_cascadeCurrent.IsValid) return;
        var current = FloatsToBytes(_lodTransform.CascadeDataCurrent);
        var source = FloatsToBytes(_lodTransform.CascadeDataSource);
        _device.BufferUpdate(_cascadeCurrent, 0, (uint)current.Length, current);
        _device.BufferUpdate(_cascadeSource, 0, (uint)source.Length, source);
    }

    private static byte[] FloatsToBytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private Array CollectInputs(string group) => CollectDictionaryCalls(group, "get_injection");

    private Array CollectDictionaryCalls(string group, string method)
    {
        var result = new Array();
        foreach (var node in GetTree().GetNodesInGroup(group))
        {
            if (!node.HasMethod(method)) continue;
            var value = node.Call(method);
            if (value.VariantType == Variant.Type.Dictionary && value.AsGodotDictionary().Count > 0) result.Add(value);
        }
        return result;
    }

    private Array CollectObjects(string group, string method)
    {
        var result = new Array();
        foreach (var node in GetTree().GetNodesInGroup(group))
            if (node.HasMethod(method)) result.Add(node);
        return result;
    }

    private DirectionalLight3D? FindDirectionalLight()
    {
        foreach (var node in GetTree().GetNodesInGroup("crest_main_light"))
            if (node is DirectionalLight3D light) return light;
        var scene = GetTree().CurrentScene;
        if (scene == null) return null;
        foreach (var node in scene.FindChildren("*", "DirectionalLight3D", true, false))
            if (node is DirectionalLight3D light) return light;
        return null;
    }

    private Texture2DArrayRD GetFallbackTextureArray()
    {
        if (_fallbackTextureArray != null) return _fallbackTextureArray;
        var format = new RDTextureFormat
        {
            Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat, Width = 1, Height = 1, ArrayLayers = 2,
            TextureType = RenderingDevice.TextureType.Type2DArray,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit,
        };
        var zero = new byte[8];
        _fallbackTextureArrayRid = _device!.TextureCreate(format, new RDTextureView(), new Array<byte[]> { zero, zero });
        _fallbackTextureArray = new Texture2DArrayRD { TextureRdRid = _fallbackTextureArrayRid };
        return _fallbackTextureArray;
    }

    private Texture2D GetFallbackTexture2D()
    {
        if (_fallbackTexture2D != null) return _fallbackTexture2D;
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgbaf);
        image.Fill(Colors.Transparent);
        return _fallbackTexture2D = ImageTexture.CreateFromImage(image);
    }

    private void SyncStaticMaterialParams()
    {
        if (OceanMaterial == null || AnimatedWaves == null) return;
        var fallback = GetFallbackTextureArray();
        OceanMaterial.SetShaderParameter("ld_animated_waves", AnimatedWaves.texture_array);
        OceanMaterial.SetShaderParameter("ld_foam", Foam != null ? Foam.texture_array : fallback);
        OceanMaterial.SetShaderParameter("ld_sea_floor_depth", Depth != null ? Depth.texture_array : fallback);
        OceanMaterial.SetShaderParameter("ld_flow", Flow != null ? Flow.texture_array : fallback);
        OceanMaterial.SetShaderParameter("ld_shadow", Shadow != null ? Shadow.texture_array : fallback);
        OceanMaterial.SetShaderParameter("ld_clip_surface", ClipSurface != null ? ClipSurface.texture_array : fallback);
        OceanMaterial.SetShaderParameter("ld_albedo", Albedo != null ? Albedo.texture_array : fallback);
        OceanMaterial.SetShaderParameter("slice_count", (float)LodCount);
        OceanMaterial.SetShaderParameter("base_mesh_density", LodDataResolution * 0.25f / GeometryDownSampleFactor);
        OceanMaterial.SetShaderParameter("foam_texture", LoadTextureMipped("res://addons/crest/textures/Foam2.png"));
        OceanMaterial.SetShaderParameter("normals_texture", LoadTextureMipped("res://addons/crest/textures/wave_normals.png"));
        OceanMaterial.SetShaderParameter("caustics_texture", LoadTextureMipped("res://addons/crest/textures/caustics.png"));
        OceanMaterial.SetShaderParameter("planar_reflection", GetFallbackTexture2D());
        OceanMaterial.SetShaderParameter("enable_foam", Foam != null ? 1.0f : 0.0f);
        OceanMaterial.SetShaderParameter("enable_shadow", Shadow != null ? 1.0f : 0.0f);
        OceanMaterial.SetShaderParameter("enable_clip_surface", ClipSurface != null ? 1.0f : 0.0f);
        OceanMaterial.SetShaderParameter("enable_sea_floor_depth", Depth != null ? 1.0f : 0.0f);
        OceanMaterial.SetShaderParameter("enable_albedo", Albedo != null ? 1.0f : 0.0f);
    }

    private static Texture2D LoadTextureMipped(string path)
    {
        var bytes = FileAccess.GetFileAsBytes(path);
        var image = new Image();
        if (bytes.Length > 0 && image.LoadPngFromBuffer(bytes) == Error.Ok)
        {
            image.GenerateMipmaps();
            return ImageTexture.CreateFromImage(image);
        }
        var imported = GD.Load<Texture2D>(path);
        if (imported != null) return imported;
        GD.PushError($"CrestOceanRenderer: failed to load texture {path}.");
        var fallback = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        fallback.Fill(Colors.Transparent);
        return ImageTexture.CreateFromImage(fallback);
    }

    private void SyncMaterialParams()
    {
        if (OceanMaterial == null || _lodTransform == null || TilesRoot == null) return;
        var a = new Array<Vector4>();
        var b = new Array<Vector4>();
        for (var i = 0; i < CrestConstantsCs.CascadeParamsCount; i++)
        {
            var o = i * 8;
            var d = _lodTransform.CascadeDataCurrent;
            a.Add(new Vector4(d[o], d[o + 1], d[o + 2], d[o + 3]));
            b.Add(new Vector4(d[o + 4], d[o + 5], d[o + 6], d[o + 7]));
        }
        OceanMaterial.SetShaderParameter("cascade_data_a", a);
        OceanMaterial.SetShaderParameter("cascade_data_b", b);
        OceanMaterial.SetShaderParameter("ocean_center_pos", TilesRoot.GlobalPosition);
        OceanMaterial.SetShaderParameter("ocean_scale", OceanScale);
        OceanMaterial.SetShaderParameter("crest_time", ExternalTime);
        var density = LodDataResolution * 0.25f / GeometryDownSampleFactor;
        var blackPoint = 0.4f / (density / 8.0f);
        OceanMaterial.SetShaderParameter("lod_alpha_black_point_fade", blackPoint);
        OceanMaterial.SetShaderParameter("lod_alpha_black_point_white_point_fade", 1.0f - 2.0f * blackPoint);
        OceanMaterial.SetShaderParameter("mesh_scale_lerp", ViewerAltitudeLevelAlpha);
        OceanMaterial.SetShaderParameter("ocean_level", OceanLevel);
        OceanMaterial.SetShaderParameter("planar_reflection", ExternalPlanarReflection ?? GetFallbackTexture2D());
        OceanMaterial.SetShaderParameter("planar_reflection_intensity", ExternalPlanarReflection != null ? ExternalPlanarReflectionIntensity : 0.0f);
        OceanMaterial.SetShaderParameter("force_underwater", ViewerHeightAboveWater < -2.0f ? 1.0f : ViewerHeightAboveWater > 2.0f ? -1.0f : 0.0f);

        var overrides = OS.GetEnvironment("CREST_MAT_OVERRIDES");
        foreach (var entry in overrides.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = entry.Split('=', 2);
            if (pair.Length == 2 && float.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                OceanMaterial.SetShaderParameter(pair[0], value);
        }

        var light = FindDirectionalLight();
        if (light != null)
        {
            OceanMaterial.SetShaderParameter("light_dir", -light.GlobalTransform.Basis.Z.Normalized());
            OceanMaterial.SetShaderParameter("light_color", light.LightColor * light.LightEnergy);
        }
        var environment = GetViewport()?.World3D?.Environment;
        if (environment != null)
        {
            var ambient = environment.AmbientLightColor * environment.AmbientLightEnergy;
            if (environment.AmbientLightSource == Environment.AmbientSource.Sky)
                ambient = new Color(0.5f, 0.6f, 0.7f) * Mathf.Max(environment.AmbientLightEnergy, 1.0f);
            OceanMaterial.SetShaderParameter("ambient_light", ambient);
        }
    }
}
