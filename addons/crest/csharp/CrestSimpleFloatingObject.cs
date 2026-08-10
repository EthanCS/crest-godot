using Godot;

namespace Crest.Godot;

/// C# buoyancy component using the migrated analytic collision provider.
[Tool]
public partial class CrestSimpleFloatingObject : CrestFloatingObjectBase
{
    [Export] public float raise_object { get; set; } = 1.0f;
    [Export] public float buoyancy_coeff { get; set; } = 3.0f;
    [Export] public float buoyancy_torque { get; set; } = 8.0f;
    [Export] public float accelerate_downhill { get; set; }
    [Export] public float maximum_buoyancy_force { get; set; }
    [Export] public float force_height_offset { get; set; } = -0.3f;
    [Export] public float drag_in_water_up { get; set; } = 3.0f;
    [Export] public float drag_in_water_right { get; set; } = 2.0f;
    [Export] public float drag_in_water_forward { get; set; } = 1.0f;
    [Export] public float drag_in_water_rotational { get; set; } = 0.2f;

    private RigidBody3D? _body;
    public bool InWater { get; private set; }

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;
        _body = FindBody();
        if (_body == null)
            GD.PushWarning("CrestSimpleFloatingObject: no RigidBody3D found; disabled.");
    }

    public override void _PhysicsProcess(double delta)
    {
        var ocean = CrestOceanRendererFacade.Instance;
        if (Engine.IsEditorHint() || _body == null || ocean == null)
            return;
        var position = _body.GlobalPosition;
        var xz = new Vector2(position.X, position.Z);
        var time = ocean.CurrentTime;
        var displacement = CrestCollisionCs.SampleDisplacement(xz, time);
        var normal = CrestCollisionCs.SampleNormal(xz, time, Mathf.Max(object_width * 0.5f, 0.1f));
        CrestCollisionCs.SampleHeightAndVelocity(xz, time, delta, ocean.OceanLevel, out _, out var waterVelocityY);
        var relativeVelocity = _body.LinearVelocity - new Vector3(0, waterVelocityY, 0);
        var bottomDepth = displacement.Y + ocean.OceanLevel - position.Y + raise_object;
        InWater = bottomDepth > 0.0f;
        InWaterState = InWater;
        if (!InWater) return;

        var mass = _body.Mass;
        var gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        var buoyancy = new Vector3(0, buoyancy_coeff * bottomDepth * bottomDepth * bottomDepth, 0);
        if (maximum_buoyancy_force > 0.0f)
            buoyancy = buoyancy.LimitLength(maximum_buoyancy_force);
        _body.ApplyCentralForce(buoyancy * mass);
        if (accelerate_downhill > 0.0f)
            _body.ApplyCentralForce(accelerate_downhill * gravity * new Vector3(normal.X, 0, normal.Z) * mass);

        var forcePosition = position + force_height_offset * Vector3.Up;
        var offset = forcePosition - position;
        var upDrag = drag_in_water_up * -relativeVelocity.Dot(Vector3.Up) * Vector3.Up * mass;
        _body.ApplyForce(upDrag, offset);
        var right = _body.GlobalTransform.Basis.X;
        _body.ApplyForce(drag_in_water_right * -relativeVelocity.Dot(right) * right * mass, offset);
        var forward = -_body.GlobalTransform.Basis.Z;
        _body.ApplyForce(drag_in_water_forward * -relativeVelocity.Dot(forward) * forward * mass, offset);
        _body.ApplyTorque(_body.GlobalTransform.Basis.Y.Cross(normal) * buoyancy_torque * mass);
        _body.ApplyTorque(-drag_in_water_rotational * _body.AngularVelocity * mass);
    }

    private RigidBody3D? FindBody()
    {
        Node? node = this;
        while (node != null)
        {
            if (node is RigidBody3D body) return body;
            node = node.GetParent();
        }
        return null;
    }

    public override bool in_water() => InWater;
    public override Vector3 get_velocity() => _body?.LinearVelocity ?? Vector3.Zero;
}
