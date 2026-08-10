using Godot;

namespace Crest.Godot;

/// C# buoyancy component using the migrated analytic collision provider.
[Tool]
public partial class CrestSimpleFloatingObject : CrestFloatingObjectBase
{
    [Export] public float _raiseObject { get; set; } = 1.0f;
    [Export] public float _buoyancyCoeff { get; set; } = 3.0f;
    [Export] public float _boyancyTorque { get; set; } = 8.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _accelerateDownhill { get; set; }
    [Export] public float _maximumBuoyancyForce { get; set; } = float.PositiveInfinity;
    [Export] public float _objectWidth { get; set; } = 3.0f;
    [Export] public float _forceHeightOffset { get; set; } = -0.3f;
    [Export] public float _dragInWaterUp { get; set; } = 3.0f;
    [Export] public float _dragInWaterRight { get; set; } = 2.0f;
    [Export] public float _dragInWaterForward { get; set; } = 1.0f;
    [Export] public float _dragInWaterRotational { get; set; } = 0.2f;

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
        var normal = CrestCollisionCs.SampleNormal(xz, time, Mathf.Max(_objectWidth * 0.5f, 0.1f));
        CrestCollisionCs.SampleHeightAndVelocity(xz, time, delta, ocean.OceanLevel, out _, out var waterVelocityY);
        var relativeVelocity = _body.LinearVelocity - new Vector3(0, waterVelocityY, 0);
        var bottomDepth = displacement.Y + ocean.OceanLevel - position.Y + _raiseObject;
        InWater = bottomDepth > 0.0f;
        InWaterState = InWater;
        if (!InWater) return;

        var mass = _body.Mass;
        var gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        var buoyancy = new Vector3(0, _buoyancyCoeff * bottomDepth * bottomDepth * bottomDepth, 0);
        if (_maximumBuoyancyForce > 0.0f)
            buoyancy = buoyancy.LimitLength(_maximumBuoyancyForce);
        _body.ApplyCentralForce(buoyancy * mass);
        if (_accelerateDownhill > 0.0f)
            _body.ApplyCentralForce(_accelerateDownhill * gravity * new Vector3(normal.X, 0, normal.Z) * mass);

        var forcePosition = position + _forceHeightOffset * Vector3.Up;
        var offset = forcePosition - position;
        var upDrag = _dragInWaterUp * -relativeVelocity.Dot(Vector3.Up) * Vector3.Up * mass;
        _body.ApplyForce(upDrag, offset);
        var right = _body.GlobalTransform.Basis.X;
        _body.ApplyForce(_dragInWaterRight * -relativeVelocity.Dot(right) * right * mass, offset);
        var forward = -_body.GlobalTransform.Basis.Z;
        _body.ApplyForce(_dragInWaterForward * -relativeVelocity.Dot(forward) * forward * mass, offset);
        _body.ApplyTorque(_body.GlobalTransform.Basis.Y.Cross(normal) * _boyancyTorque * mass);
        _body.ApplyTorque(-_dragInWaterRotational * _body.AngularVelocity * mass);
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
