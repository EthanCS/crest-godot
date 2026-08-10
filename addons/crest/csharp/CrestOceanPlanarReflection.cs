using Godot;
using System.Collections.Generic;

namespace Crest.Godot;

/// Mirrored-camera planar reflection with a second viewport correcting the
/// horizontal flip required by a right-handed Camera3D transform.
[Tool]
public partial class CrestOceanPlanarReflection : Node3D
{
    public Texture2D? ReflectionTexture => _outputViewport?.GetTexture();
    private Vector2I _reflectionTextureSize = new(512, 512);
    private int _refreshPerFrames = 1;
    private uint _cullMask = 1;

    [Export]
    public Vector2I reflection_texture_size
    {
        get => _reflectionTextureSize;
        set
        {
            _reflectionTextureSize = new Vector2I(Mathf.Max(16, value.X), Mathf.Max(16, value.Y));
            ApplySize();
        }
    }

    [Export(PropertyHint.Range, "1,60,1")]
    public int refresh_per_frames
    {
        get => _refreshPerFrames;
        set => _refreshPerFrames = Mathf.Max(1, value);
    }

    [Export(PropertyHint.Layers3DRender)]
    public uint cull_mask
    {
        get => _cullMask;
        set
        {
            _cullMask = value;
            if (_camera != null) _camera.CullMask = value;
        }
    }

    [Export] public float clip_plane_offset { get; set; } = 0.07f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float intensity { get; set; } = 1.0f;

    private SubViewport? _renderViewport;
    private SubViewport? _outputViewport;
    private Camera3D? _camera;
    private Sprite2D? _flipSprite;
    private int _frame;
    private bool _rendering;
    private readonly Dictionary<VisualInstance3D, uint> _hiddenChunks = new();

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;
        ProcessPriority = 110;
        _renderViewport = new SubViewport
        {
            Name = "CrestReflectionRender",
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            World3D = GetViewport().World3D,
        };
        AddChild(_renderViewport, false, InternalMode.Back);
        _camera = new Camera3D { Name = "ReflectionCamera", CullMask = _cullMask };
        _renderViewport.AddChild(_camera);

        _outputViewport = new SubViewport
        {
            Name = "CrestReflectionOutput",
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = true,
        };
        AddChild(_outputViewport, false, InternalMode.Back);
        _flipSprite = new Sprite2D
        {
            Name = "Flip",
            FlipH = true,
            Texture = _renderViewport.GetTexture(),
        };
        _outputViewport.AddChild(_flipSprite);
        ApplySize();
    }

    public override void _ExitTree()
    {
        RestoreChunks();
        CrestOceanRendererFacade.Instance?.SetPlanarReflection(null, 0.0f);
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (_renderViewport == null || _outputViewport == null || _camera == null) return;
        var ocean = CrestOceanRendererFacade.Instance;
        var sourceCamera = GetViewport()?.GetCamera3D();
        if (ocean == null || sourceCamera == null) return;
        ocean.SetPlanarReflection(_outputViewport.GetTexture(), intensity);
        if (intensity <= 0.001f || _rendering) return;
        _frame++;
        if (_frame % _refreshPerFrames != 0) return;
        UpdateReflectionCamera(sourceCamera, ocean.OceanLevel + clip_plane_offset);
        ApplySize();
        RenderOnce(ocean);
    }

    private async void RenderOnce(CrestOceanRendererFacade ocean)
    {
        _rendering = true;
        var hiddenLayer = HiddenLayerBit();
        foreach (var chunk in ocean.Chunks)
        {
            if (!GodotObject.IsInstanceValid(chunk)) continue;
            _hiddenChunks[chunk] = chunk.Layers;
            chunk.Layers = hiddenLayer;
        }
        _renderViewport!.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        RestoreChunks();
        _rendering = false;
    }

    private void RestoreChunks()
    {
        foreach (var pair in _hiddenChunks)
            if (GodotObject.IsInstanceValid(pair.Key)) pair.Key.Layers = pair.Value;
        _hiddenChunks.Clear();
    }

    private uint HiddenLayerBit()
    {
        var freeBits = (~_cullMask) & 0xFFFFFu;
        return freeBits == 0 ? 1u << 19 : freeBits & (0u - freeBits);
    }

    private void UpdateReflectionCamera(Camera3D source, float planeY)
    {
        _camera!.Projection = source.Projection;
        _camera.Fov = source.Fov;
        _camera.Size = source.Size;
        _camera.FrustumOffset = source.FrustumOffset;
        _camera.Near = source.Near;
        _camera.Far = source.Far;
        var transform = source.GlobalTransform;
        var origin = transform.Origin;
        origin.Y = 2.0f * planeY - origin.Y;
        var basis = transform.Basis;
        var reflected = new Basis(
            new Vector3(-basis.X.X, basis.X.Y, -basis.X.Z),
            new Vector3(basis.Y.X, -basis.Y.Y, basis.Y.Z),
            new Vector3(basis.Z.X, -basis.Z.Y, basis.Z.Z));
        _camera.GlobalTransform = new Transform3D(reflected, origin);
    }

    private void ApplySize()
    {
        if (_renderViewport == null || _outputViewport == null || _flipSprite == null) return;
        var target = _reflectionTextureSize;
        var viewportSize = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        if (viewportSize.X > 0.0f && viewportSize.Y > 0.0f)
            target.Y = Mathf.Max(16, Mathf.RoundToInt(target.X * viewportSize.Y / viewportSize.X));
        if (_renderViewport.Size == target) return;
        _renderViewport.Size = target;
        _outputViewport.Size = target;
        _flipSprite.Position = new Vector2(target.X, target.Y) * 0.5f;
    }
}
