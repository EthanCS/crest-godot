using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// Shifts top-level 3D scene nodes when the viewer travels beyond the
/// configured XZ threshold, keeping physics and rendering near the origin.
[Tool, GlobalClass]
public partial class CrestFloatingOrigin : Node3D
{
    [Signal]
    public delegate void OriginShiftedEventHandler(Vector3 offset);

    [Export] public int _version { get; set; }
    [Export] public float _threshold { get; set; } = 16384.0f;
    [Export] public float _physicsThreshold { get; set; } = 1000.0f;
    [Export] public float _defaultSleepThreshold { get; set; } = 0.14f;
    [Export] public Array<Node3D> _overrideTransformList { get; set; } = new();
    [Export] public Array<GpuParticles3D> _overrideParticleSystemList { get; set; } = new();
    [Export] public Array<RigidBody3D> _overrideRigidbodyList { get; set; } = new();
    [Export] public Array<CrestShapeGerstner> _overrideGerstnerList { get; set; } = new();
    [Export] public bool _waveCompatibiltyMode { get; set; }
    [Export] public CrestShiftingOriginDebugFields _debug { get; set; } = new();
    public Node3D? Viewpoint { get; set; }

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        if (Engine.IsEditorHint()) return;
        var viewer = Viewpoint ?? GetViewport()?.GetCamera3D();
        if (viewer == null) return;
        var position = viewer.GlobalPosition;
        if (Mathf.Max(Mathf.Abs(position.X), Mathf.Abs(position.Z)) < _threshold) return;
        var offset = new Vector3(position.X, 0.0f, position.Z);
        var root = GetTree()?.CurrentScene;
        if (root == null) return;
        foreach (var child in root.GetChildren())
            if (child is Node3D spatial && spatial != this && spatial is not CrestFloatingOrigin)
                spatial.GlobalPosition -= offset;
        EmitSignal(SignalName.OriginShifted, offset);
    }
}

[GlobalClass]
public partial class CrestShiftingOriginDebugFields : Resource
{
    [Export] public bool _pauseOnShift { get; set; }
    [Export] public bool _pauseBeforeShift { get; set; }
    [Export] public bool _logOnShift { get; set; }
}
