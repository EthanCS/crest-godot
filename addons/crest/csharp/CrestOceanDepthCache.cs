using Godot;
using Godot.Collections;
using System.Collections.Generic;

namespace Crest.Godot;

/// Terrain height cache for the sea-floor depth simulation. A supplied
/// heightmap is used directly; otherwise a top-down viewport captures world
/// height through the depth-cache override shader.
[Tool, GlobalClass]
public partial class CrestOceanDepthCache : Node3D
{
    public enum RefreshMode
    {
        Realtime,
        OnStart,
        OnDemand,
    }

    [Export] public int resolution { get; set; } = 512;
    [Export] public Vector2 cache_size { get; set; } = new(256.0f, 256.0f);
    [Export] public float camera_max_terrain_height { get; set; } = 100.0f;
    [Export] public RefreshMode refresh_mode { get; set; } = RefreshMode.OnStart;
    [Export(PropertyHint.Layers3DRender)] public uint layers { get; set; } = 1;
    [Export] public Texture2D? heightmap_texture { get; set; }

    private ImageTexture? _cacheTexture;
    private SubViewport? _viewport;
    private Camera3D? _camera;
    private ShaderMaterial? _heightMaterial;
    private bool _captureInFlight;
    private float _captureHeightMin;
    private float _captureHeightRange = 1.0f;

    public override void _EnterTree() => AddToGroup("crest_depth_input");

    public override void _Ready()
    {
        if (!Engine.IsEditorHint() && refresh_mode == RefreshMode.OnStart)
            PopulateCache();
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (!Engine.IsEditorHint() && refresh_mode == RefreshMode.Realtime)
            PopulateCache();
    }

    public Dictionary GetInjection()
    {
        var useHeightmap = heightmap_texture != null;
        return new Dictionary
        {
            ["rect_center"] = new Vector2(GlobalPosition.X, GlobalPosition.Z),
            ["rect_half_size"] = cache_size * 0.5f,
#pragma warning disable CS8604
            ["texture"] = useHeightmap ? heightmap_texture : _cacheTexture,
#pragma warning restore CS8604
            ["height_offset"] = useHeightmap ? GlobalPosition.Y : 0.0f,
            ["sea_level_offset"] = 0.0f,
            ["mode"] = 0.0f,
        };
    }

    public async void PopulateCache()
    {
        if (Engine.IsEditorHint() || heightmap_texture != null || _captureInFlight)
            return;
        _captureInFlight = true;
        EnsureCaptureRig();
        if (_viewport == null || _heightMaterial == null)
        {
            _captureInFlight = false;
            return;
        }

        var overridden = new List<(GeometryInstance3D Geometry, Material? Material)>();
        foreach (var geometry in CollectTerrainGeometry())
        {
            overridden.Add((geometry, geometry.MaterialOverride));
            geometry.MaterialOverride = _heightMaterial;
        }

        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        foreach (var pair in overridden)
            if (IsInstanceValid(pair.Geometry)) pair.Geometry.MaterialOverride = pair.Material;
        _captureInFlight = false;

        if (!IsInsideTree() || _viewport == null) return;
        var image = _viewport.GetTexture().GetImage();
        if (image == null || image.IsEmpty())
        {
            GD.PushWarning("CrestOceanDepthCache: capture failed, no image read back.");
            return;
        }
        image = DecodeCapturedHeights(image);
        RecyclePreviousTexture();
        _cacheTexture = ImageTexture.CreateFromImage(image);
    }

    private void EnsureCaptureRig()
    {
        if (_viewport == null)
        {
            _viewport = new SubViewport
            {
                Name = "CrestDepthCacheViewport",
                GuiDisableInput = true,
                HandleInputLocally = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
                World3D = GetViewport().World3D,
            };
            _camera = new Camera3D
            {
                Name = "CrestDepthCacheCamera",
                Projection = Camera3D.ProjectionType.Orthogonal,
                Current = true,
                Environment = new Environment
                {
                    BackgroundMode = Environment.BGMode.Color,
                    TonemapMode = Environment.ToneMapper.Linear,
                    GlowEnabled = false,
                    FogEnabled = false,
                    VolumetricFogEnabled = false,
                    SsaoEnabled = false,
                    SsilEnabled = false,
                    SsrEnabled = false,
                    SdfgiEnabled = false,
                },
            };
            _viewport.AddChild(_camera);
            AddChild(_viewport, false, InternalMode.Back);
        }

        var sizeX = Mathf.Max(cache_size.X, 0.01f);
        var sizeY = Mathf.Max(cache_size.Y, 0.01f);
        var longest = Mathf.Max(sizeX, sizeY);
        _viewport.Size = new Vector2I(
            Mathf.Max(1, Mathf.RoundToInt(resolution * sizeX / longest)),
            Mathf.Max(1, Mathf.RoundToInt(resolution * sizeY / longest)));
        _camera!.Size = sizeY;
        _camera.Near = 0.05f;
        _camera.Far = camera_max_terrain_height + 1000.0f;
        _camera.CullMask = layers;
        _camera.GlobalPosition = GlobalPosition + new Vector3(0.0f, camera_max_terrain_height, 0.0f);
        _camera.GlobalRotation = new Vector3(-Mathf.Pi * 0.5f, 0.0f, 0.0f);

        var heightSpan = Mathf.Max(1000.0f, camera_max_terrain_height);
        _captureHeightMin = GlobalPosition.Y - heightSpan;
        var heightMax = GlobalPosition.Y + heightSpan;
        _captureHeightRange = Mathf.Max(heightMax - _captureHeightMin, 0.01f);
        var encodedBaseline = Mathf.Clamp(
            (CrestConstantsCs.OceanDepthBaseline - _captureHeightMin) / _captureHeightRange, 0.0f, 1.0f);
        _camera.Environment!.BackgroundColor = PackHeight(encodedBaseline).SrgbToLinear();

        // SubViewport material updates can freeze after the first render.
        // A fresh material makes every capture's encoding range immutable.
        _heightMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://addons/crest/shaders/depth_cache_height.gdshader"),
        };
        _heightMaterial.SetShaderParameter("height_min", _captureHeightMin);
        _heightMaterial.SetShaderParameter("height_range", _captureHeightRange);
    }

    private List<GeometryInstance3D> CollectTerrainGeometry()
    {
        var result = new List<GeometryInstance3D>();
        var root = GetTree()?.Root;
        if (root == null) return result;
        foreach (var node in root.FindChildren("*", "GeometryInstance3D", true, false))
        {
            if (node is not GeometryInstance3D geometry || (geometry.Layers & layers) == 0)
                continue;
            if (geometry is CrestOceanChunkRendererCs) continue;
            result.Add(geometry);
        }
        return result;
    }

    private void RecyclePreviousTexture()
    {
        if (_cacheTexture == null) return;
        CrestOceanRendererFacade.Instance?.InvalidateDepthTexture(_cacheTexture);
    }

    private Image DecodeCapturedHeights(Image source)
    {
        var decoded = Image.CreateEmpty(source.GetWidth(), source.GetHeight(), false, Image.Format.Rf);
        for (var y = 0; y < source.GetHeight(); y++)
        for (var x = 0; x < source.GetWidth(); x++)
        {
            var packed = source.GetPixel(x, y);
            var encoded = packed.R + packed.G / 255.0f + packed.B / 65025.0f;
            var height = _captureHeightMin + encoded * _captureHeightRange;
            decoded.SetPixel(x, y, new Color(height, 0.0f, 0.0f, 1.0f));
        }
        return decoded;
    }

    private static Color PackHeight(float value)
    {
        value = Mathf.Min(value, 0.9999999f);
        var r = value - Mathf.Floor(value);
        var g = value * 255.0f - Mathf.Floor(value * 255.0f);
        var b = value * 65025.0f - Mathf.Floor(value * 65025.0f);
        r -= g / 255.0f;
        g -= b / 255.0f;
        return new Color(r, g, b, 1.0f);
    }
}
