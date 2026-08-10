using Godot;

namespace Crest.Godot;

/// Shifts top-level 3D scene nodes when the viewer travels beyond the
/// configured XZ threshold, keeping physics and rendering near the origin.
[Tool, GlobalClass]
public partial class CrestFloatingOrigin : Node3D
{
    [Signal]
    public delegate void OriginShiftedEventHandler(Vector3 offset);

    [Export] public float threshold { get; set; } = 4096.0f;
    [Export] public Node3D? viewpoint { get; set; }

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        if (Engine.IsEditorHint()) return;
        var viewer = viewpoint ?? GetViewport()?.GetCamera3D();
        if (viewer == null) return;
        var position = viewer.GlobalPosition;
        if (Mathf.Max(Mathf.Abs(position.X), Mathf.Abs(position.Z)) < threshold) return;
        var offset = new Vector3(position.X, 0.0f, position.Z);
        var root = GetTree()?.CurrentScene;
        if (root == null) return;
        foreach (var child in root.GetChildren())
            if (child is Node3D spatial && spatial != this && spatial is not CrestFloatingOrigin)
                spatial.GlobalPosition -= offset;
        EmitSignal(SignalName.OriginShifted, offset);
    }
}
