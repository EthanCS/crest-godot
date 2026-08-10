using Godot;
using System.Collections.Generic;
using Godot.Collections;

namespace Crest.Godot;

/// Multi-point buoyancy where direct Node3D children are force probes.
[Tool]
public partial class CrestBoatProbes : CrestFloatingObjectBase
{
    [Export] public int _version { get; set; }
    [Export] public Vector3 _centerOfMass { get; set; }
    [Export] public Array<CrestFloaterForcePoint> _forcePoints { get; set; } = new();
    [Export] public float _forceHeightOffset { get; set; }
    [Export] public float _forceMultiplier { get; set; } = 10.0f;
    [Export] public float _minSpatialLength { get; set; } = 12.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _turningHeel { get; set; } = 0.35f;
    [Export] public float _maximumBuoyancyForce { get; set; } = float.PositiveInfinity;
    [Export] public float _dragInWaterUp { get; set; } = 3.0f;
    [Export] public float _dragInWaterRight { get; set; } = 2.0f;
    [Export] public float _dragInWaterForward { get; set; } = 1.0f;
    [Export] public float _enginePower { get; set; } = 7.0f;
    [Export] public float _turnPower { get; set; } = 0.5f;
    [Export] public bool _playerControlled { get; set; } = true;
    [Export] public float _engineBias { get; set; }
    [Export] public float _turnBias { get; set; }
    [Export] public CrestBoatProbesDebugFields _debug { get; set; } = new();

    private RigidBody3D? _body;
    private bool _inWater;
    private float _gravity;
    private readonly List<Node3D> _probes = new();
    private readonly List<float> _probeWeights = new();
    private float _totalWeight;

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;
        _body = FindBody();
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        foreach (var point in _forcePoints)
        {
            var probe = point != null && !point._transform.IsEmpty
                ? GetNodeOrNull<Node3D>(point._transform) : null;
            if (probe != null)
            {
                _probes.Add(probe);
                _probeWeights.Add(Mathf.Max(point!._weight, 0.0f));
            }
        }
        if (_probes.Count == 0)
            foreach (var child in GetChildren()) if (child is Node3D probe)
            {
                _probes.Add(probe);
                _probeWeights.Add(1.0f);
            }
        foreach (var weight in _probeWeights) _totalWeight += weight;
        _totalWeight = Mathf.Max(_totalWeight, 0.001f);
        if (_body != null)
        {
            _body.CenterOfMassMode = RigidBody3D.CenterOfMassModeEnum.Custom;
            _body.CenterOfMass = _centerOfMass;
        }
        if (_body == null)
            GD.PushWarning("CrestBoatProbes: no RigidBody3D found on self or parents; disabled.");
    }

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        var ocean = CrestOceanRendererFacade.Instance;
        if (Engine.IsEditorHint() || _body == null || ocean == null) return;
        _inWater = false;
        for (var index = 0; index < _probes.Count; index++)
        {
            var probe = _probes[index];
            var position = probe.GlobalPosition + Vector3.Up * _forceHeightOffset;
            position += _body.GlobalBasis * new Vector3(0, _centerOfMass.Y, 0);
            CrestCollisionCs.SampleHeight(new Vector2(position.X, position.Z), ocean.CurrentTime,
                ocean.OceanLevel, out var waterHeight);
            var submersion = waterHeight - position.Y;
            if (submersion <= 0.0f) continue;
            _inWater = true;
            var probeMass = _body.Mass / Mathf.Max(_probes.Count, 1);
            // Crest's probe force is Archimedes force, not mass-scaled
            // acceleration: density * gravity * displaced depth.
            var force = Vector3.Up * (1000.0f * _gravity * submersion * _forceMultiplier
                * _probeWeights[index] / _totalWeight);
            var pointVelocity = _body.LinearVelocity +
                _body.AngularVelocity.Cross(position - _body.GlobalPosition);
            var localVelocity = _body.GlobalTransform.Basis.Inverse() * pointVelocity;
            var localDrag = new Vector3(_dragInWaterRight, _dragInWaterUp, _dragInWaterForward);
            force -= _body.GlobalTransform.Basis * (localVelocity * localDrag) * probeMass;
            _body.ApplyForce(force, position - _body.GlobalPosition);
        }
        InWaterState = _inWater;
    }

    public override bool in_water() => _inWater;
    public override Vector3 get_velocity() => _body?.LinearVelocity ?? Vector3.Zero;

    private RigidBody3D? FindBody()
    {
        for (Node? node = this; node != null; node = node.GetParent())
            if (node is RigidBody3D body) return body;
        return null;
    }
}

[GlobalClass]
public partial class CrestFloaterForcePoint : Resource
{
    [Export] public NodePath _transform { get; set; } = new();
    [Export] public float _weight { get; set; } = 1.0f;
}

[GlobalClass]
public partial class CrestBoatProbesDebugFields : Resource
{
    [Export] public bool _drawQueries { get; set; }
}
