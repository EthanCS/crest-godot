using Crest.Godot;
using Godot;

public partial class CrestDemoHugeFloater : RigidBody3D
{
    [Export] public Vector2 orbit_center { get; set; } = Vector2.Zero;
    [Export] public float orbit_radius { get; set; } = 30.0f;
    [Export] public float orbit_speed { get; set; } = 1.5f;
    [Export] public float steer_strength { get; set; } = 2.0f;

    public override void _Ready()
    {
        AddChild(new CrestSimpleFloatingObject
        {
            object_width = 30.0f,
            raise_object = 2.0f,
            buoyancy_coeff = 3.0f,
            buoyancy_torque = 8.0f,
        });
    }

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        if (GetTree().Paused) return;
        var position = new Vector2(GlobalPosition.X, GlobalPosition.Z);
        var toCenter = orbit_center - position;
        var distance = toCenter.Length();
        if (distance < 0.1f) return;
        var tangent = new Vector2(-toCenter.Y, toCenter.X).Normalized();
        var velocity = new Vector2(LinearVelocity.X, LinearVelocity.Z);
        var steering = (tangent * orbit_speed - velocity) * steer_strength;
        var centripetal = -toCenter.Normalized() * (orbit_speed * orbit_speed / Mathf.Max(distance, 1.0f));
        ApplyCentralForce(new Vector3(steering.X + centripetal.X, 0.0f,
            steering.Y + centripetal.Y) * Mass);
    }
}
