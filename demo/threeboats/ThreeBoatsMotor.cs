using Crest.Godot;
using Godot;

/// Engine and helm used by the Three Boats example. Forces are expressed as
/// accelerations, matching Unity Crest's ForceMode.Acceleration setup.
public partial class ThreeBoatsMotor : Node
{
    [Export] public float EnginePower { get; set; } = 11.0f;
    [Export] public float TurnPower { get; set; } = 1.3f;
    [Export] public float ThrottleBias { get; set; }
    [Export] public float SteerBias { get; set; }
    [Export] public bool PlayerControlled { get; set; }
    [Export] public float ForceHeightOffset { get; set; } = -0.3f;

    private RigidBody3D? _body;

    public override void _Ready() => _body = GetParentOrNull<RigidBody3D>();

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        var ocean = CrestOceanRendererFacade.Instance;
        if (_body == null || ocean == null) return;

        CrestCollisionCs.SampleHeight(new Vector2(_body.GlobalPosition.X, _body.GlobalPosition.Z),
            ocean.CurrentTime, ocean.OceanLevel, out var height);
        if (_body.GlobalPosition.Y > height + 4.0f) return;

        var throttle = ThrottleBias;
        var steer = SteerBias;
        if (PlayerControlled)
        {
            throttle += Input.GetAxis("move_backward", "move_forward");
            var reverse = throttle < 0.0f ? -1.0f : 1.0f;
            steer += Input.GetAxis("move_left", "move_right") * reverse;
        }

        var mass = _body.Mass;
        var basis = _body.GlobalBasis;
        var forward = -basis.Z;
        var forcePoint = _body.GlobalPosition + Vector3.Up * ForceHeightOffset;
        _body.ApplyForce(forward * EnginePower * throttle * mass,
            forcePoint - _body.GlobalPosition);
        _body.ApplyTorque(basis.Y * TurnPower * steer * mass);
    }
}
