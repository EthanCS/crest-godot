using Godot;
using System.Collections.Generic;

namespace Crest.Godot;

/// Multi-point buoyancy where direct Node3D children are force probes.
[Tool]
public partial class CrestBoatProbes : CrestFloatingObjectBase
{
    [Export] public float force_multiplier { get; set; } = 1.0f;
    [Export] public float force_height_offset { get; set; }
    [Export] public float drag { get; set; } = 1.0f;

    private RigidBody3D? _body;
    private bool _inWater;
    private float _gravity;
    private readonly List<Node3D> _probes = new();

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;
        _body = FindBody();
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        foreach (var child in GetChildren())
            if (child is Node3D probe) _probes.Add(probe);
        if (_body == null)
            GD.PushWarning("CrestBoatProbes: no RigidBody3D found on self or parents; disabled.");
    }

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        var ocean = CrestOceanRendererFacade.Instance;
        if (Engine.IsEditorHint() || _body == null || ocean == null) return;
        _inWater = false;
        var probeCount = Mathf.Max(_probes.Count, 1);
        foreach (var probe in _probes)
        {
            var position = probe.GlobalPosition + Vector3.Up * force_height_offset;
            CrestCollisionCs.SampleHeight(new Vector2(position.X, position.Z), ocean.CurrentTime,
                ocean.OceanLevel, out var waterHeight);
            var submersion = waterHeight - position.Y;
            if (submersion <= 0.0f) continue;
            _inWater = true;
            var probeMass = _body.Mass / probeCount;
            var force = Vector3.Up * _gravity * submersion * force_multiplier * probeMass;
            var pointVelocity = _body.LinearVelocity +
                _body.AngularVelocity.Cross(position - _body.GlobalPosition);
            force -= pointVelocity * drag * probeMass;
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
