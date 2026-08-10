using Godot;

namespace Crest.Godot;

/// Camera-attached row of simulation cascade texture previews.
[Tool]
public partial class CrestOceanDebugGui : Node3D
{
    [Export] public int _version { get; set; }
    [Export] public bool _showOceanData { get; set; } = true;
    [Export] public bool _guiVisible { get; set; } = true;
    [Export] public bool _drawLodDatasActualSize { get; set; }
    [Export(PropertyHint.Range, "0,1,0.01")] public float _pausedScroll { get; set; }
    [Export] public bool _drawAnimWaves { get; set; } = true;
    [Export] public bool _drawDynWaves { get; set; }
    [Export] public bool _drawFoam { get; set; }
    [Export] public bool _drawFlow { get; set; }
    [Export] public bool _drawShadow { get; set; }
    [Export] public bool _drawSeaFloorDepth { get; set; }
    [Export] public bool _drawClipSurface { get; set; }
    public Key ToggleKey { get; set; } = Key.F9;
    public int Slice { get; set; } = 2;
    public float QuadSize { get; set; } = 1.6f;

    private Node3D? _overlay;
    private bool _visible;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Engine.IsEditorHint() || @event is not InputEventKey key ||
            !key.Pressed || key.Echo || key.Keycode != ToggleKey) return;
        SetOverlayVisible(!_visible);
    }

    public void SetOverlayVisible(bool value)
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
        var x = -QuadSize * spacing * (entries.Count - 1) * 0.5f;
        foreach (var entry in entries)
        {
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("arr", entry.Texture);
            material.SetShaderParameter("slice", (float)Slice);
            var quad = new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(QuadSize, QuadSize) },
                MaterialOverride = material,
                Position = new Vector3(x, 0.0f, 0.0f),
            };
            _overlay.AddChild(quad);
            _overlay.AddChild(new Label3D
            {
                Text = entry.Label,
                FontSize = 14,
                PixelSize = 0.012f * QuadSize,
                Position = new Vector3(x, -QuadSize * 0.62f, 0.0f),
                NoDepthTest = true,
            });
            x += QuadSize * spacing;
        }
    }
}
