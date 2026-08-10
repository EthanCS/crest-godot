using Godot;

namespace Crest.Godot;

/// Camera-attached row of simulation cascade texture previews.
[Tool]
public partial class CrestOceanDebugGui : Node3D
{
    [Export] public Key toggle_key { get; set; } = Key.F9;
    [Export(PropertyHint.Range, "0,14,1")] public int slice { get; set; } = 2;
    [Export] public float quad_size { get; set; } = 1.6f;

    private Node3D? _overlay;
    private bool _visible;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Engine.IsEditorHint() || @event is not InputEventKey key ||
            !key.Pressed || key.Echo || key.Keycode != toggle_key) return;
        set_overlay_visible(!_visible);
    }

    public void set_overlay_visible(bool value)
    {
        _visible = value;
        if (_visible) Rebuild();
        else if (_overlay != null)
        {
            _overlay.QueueFree();
            _overlay = null;
        }
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (!_visible || _overlay == null) return;
        var camera = GetViewport()?.GetCamera3D();
        if (camera == null) return;
        var transform = camera.GlobalTransform;
        _overlay.GlobalPosition = camera.GlobalPosition - transform.Basis.Z * 10.0f +
            transform.Basis.Y * 3.0f;
        _overlay.GlobalRotation = camera.GlobalRotation;
    }

    private void Rebuild()
    {
        if (_overlay != null)
        {
            _overlay.QueueFree();
            _overlay = null;
        }
        var entries = CrestOceanRendererFacade.Instance?.GetDebugTextures();
        if (entries == null || entries.Count == 0) return;
        _overlay = new Node3D { Name = "CrestDebugOverlay" };
        AddChild(_overlay);
        var shader = new Shader
        {
            Code = "shader_type spatial; render_mode unshaded, depth_test_disabled, cull_disabled; " +
                "uniform sampler2DArray arr; uniform float slice; " +
                "void fragment() { vec3 d = texture(arr, vec3(UV, slice)).rgb; ALBEDO = d * 0.5 + 0.5; }",
        };
        const float spacing = 1.35f;
        var x = -quad_size * spacing * (entries.Count - 1) * 0.5f;
        foreach (var entry in entries)
        {
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("arr", entry.Texture);
            material.SetShaderParameter("slice", (float)slice);
            var quad = new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(quad_size, quad_size) },
                MaterialOverride = material,
                Position = new Vector3(x, 0.0f, 0.0f),
            };
            _overlay.AddChild(quad);
            _overlay.AddChild(new Label3D
            {
                Text = entry.Label,
                FontSize = 14,
                PixelSize = 0.012f * quad_size,
                Position = new Vector3(x, -quad_size * 0.62f, 0.0f),
                NoDepthTest = true,
            });
            x += quad_size * spacing;
        }
    }
}
