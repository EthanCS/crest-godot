using Godot;

namespace Crest.Godot;

/// Full-screen underwater fog and analytic meniscus effect.
[Tool]
public partial class CrestUnderwaterRenderer : Node3D
{
    private const float FadeTime = 0.2f;
    private const float MeniscusRange = 2.0f;
    private static readonly Vector3 BaseFogDensity = new(0.9f, 0.3f, 0.35f);

    [Export] public bool enabled { get; set; } = true;
    [Export] public bool meniscus_enabled { get; set; } = true;
    [Export] public float depth_fog_density_factor { get; set; } = 1.0f;
    [Export] public Camera3D? camera { get; set; }

    private MeshInstance3D? _meshInstance;
    private ShaderMaterial? _material;
    private float _fade;

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;
        BuildOverlay();
        ProcessPriority = 200;
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint() || _meshInstance == null || _material == null) return;
        var activeCamera = camera ?? GetViewport().GetCamera3D();
        var underwater = false;
        var waterHeight = CrestOceanRendererFacade.Instance?.OceanLevel ?? 0.0f;
        var ocean = CrestOceanRendererFacade.Instance;
        if (enabled && activeCamera != null && ocean != null)
        {
            CrestCollisionCs.SampleHeight(new Vector2(activeCamera.GlobalPosition.X,
                activeCamera.GlobalPosition.Z), ocean.CurrentTime, ocean.OceanLevel, out waterHeight);
            underwater = activeCamera.GlobalPosition.Y < waterHeight;
        }

        _fade = Mathf.MoveToward(_fade, underwater ? 1.0f : 0.0f, (float)delta / FadeTime);
        _meshInstance.Visible = _fade > 0.001f;
        if (_meshInstance.Visible && activeCamera != null)
            SyncShader(activeCamera, waterHeight);
    }

    private void BuildOverlay()
    {
        _material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://addons/crest/shaders/underwater.gdshader"),
            RenderPriority = 100,
        };
        var quad = new QuadMesh { Size = new Vector2(2.0f, 2.0f) };
        _meshInstance = new MeshInstance3D
        {
            Name = "CrestUnderwaterQuad",
            Mesh = quad,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            IgnoreOcclusionCulling = true,
            ExtraCullMargin = 16384.0f,
            Visible = false,
        };
        AddChild(_meshInstance);
    }

    private void SyncShader(Camera3D activeCamera, float waterHeight)
    {
        _material!.SetShaderParameter("effect_strength", _fade);
        _material.SetShaderParameter("depth_fog_density", BaseFogDensity * depth_fog_density_factor);
        _material.SetShaderParameter("diffuse", new Vector3(0.0f, 0.0027f, 0.170f));
        _material.SetShaderParameter("diffuse_grazing", new Vector3(0.0f, 0.0039f, 0.169f));
        _material.SetShaderParameter("subsurface_colour", new Vector3(0.0885f, 0.497f, 0.456f));
        _material.SetShaderParameter("subsurface_base", 0.0f);
        _material.SetShaderParameter("subsurface_sun", 1.7f);
        _material.SetShaderParameter("subsurface_sun_falloff", 5.0f);

        var light = FindDirectionalLight();
        _material.SetShaderParameter("light_dir", light != null
            ? -light.GlobalTransform.Basis.Z.Normalized() : Vector3.Up);
        _material.SetShaderParameter("light_color", light != null
            ? ColorToVector(light.LightColor * light.LightEnergy) : Vector3.One);
        var environment = activeCamera.GetViewport()?.World3D?.Environment;
        _material.SetShaderParameter("ambient_light", environment != null
            ? ColorToVector(environment.AmbientLightColor * environment.AmbientLightEnergy)
            : new Vector3(0.4f, 0.45f, 0.5f));

        var meniscus = 0.0f;
        var waterlineY = -1.0f;
        if (meniscus_enabled)
        {
            var heightAbove = activeCamera.GlobalPosition.Y - waterHeight;
            meniscus = Mathf.Clamp(1.0f - Mathf.Abs(heightAbove) / MeniscusRange, 0.0f, 1.0f);
            if (meniscus > 0.001f)
            {
                var forward = -activeCamera.GlobalTransform.Basis.Z;
                var flat = new Vector3(forward.X, 0.0f, forward.Z);
                if (flat.Length() > 0.01f)
                {
                    var point = activeCamera.GlobalPosition + flat.Normalized() * 1.0e5f;
                    if (!activeCamera.IsPositionBehind(point))
                        waterlineY = activeCamera.UnprojectPosition(point).Y;
                }
            }
        }
        _material.SetShaderParameter("meniscus_strength", meniscus);
        _material.SetShaderParameter("waterline_screen_y", waterlineY);
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

    private static Vector3 ColorToVector(Color color) => new(color.R, color.G, color.B);
}
