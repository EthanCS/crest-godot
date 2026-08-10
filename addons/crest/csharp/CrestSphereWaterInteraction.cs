using Godot;
using Godot.Collections;
using System.Collections.Generic;

namespace Crest.Godot;

/// Spherical dynamic-wave interaction with velocity and submersion weighting.
[Tool]
public partial class CrestSphereWaterInteraction : Node3D
{
    private static readonly List<CrestSphereWaterInteraction> Active = new();
    public static IReadOnlyList<CrestSphereWaterInteraction> ActiveInteractions => Active;
    [Export(PropertyHint.Range, "0.01,50,0.01")] public float _radius { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "-40,40,0.01")] public float _weight { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,2,0.01")] public float _weightUpDownMul { get; set; } = 0.5f;
    [Export(PropertyHint.Range, "0,10,0.01")] public float _innerSphereMultiplier { get; set; } = 1.55f;
    [Export(PropertyHint.Range, "0,1,0.001")] public float _innerSphereOffset { get; set; } = 0.109f;
    [Export(PropertyHint.Range, "0,2,0.001")] public float _velocityOffset { get; set; } = 0.04f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _compensateForWaveMotion { get; set; } = 0.45f;
    [Export] public bool _boostLargeWaves { get; set; }
    [Export] public float _teleportSpeed { get; set; } = 500.0f;
    [Export] public float _maxSpeed { get; set; } = 100.0f;
    [Export(PropertyHint.Range, "0,10,0.1")] public float _warmUpDuration { get; set; }
    public float FoamStrength { get; set; } = 0.5f;
    public void SetFoamStrength(float value) => FoamStrength = value;

    private Vector3 _lastPosition;
    private bool _hasLast;
    private Vector3 _velocity;
    private float _age;

    public override void _EnterTree()
    {
        _age = 0.0f;
        if (!Active.Contains(this)) Active.Add(this);
        AddToGroup("crest_sphere_interaction_cs");
    }

    public override void _ExitTree() => Active.Remove(this);

    public override void _PhysicsProcess(double delta)
    {
        if (Engine.IsEditorHint()) return;
        _age += (float)delta;
        var position = GlobalPosition;
        if (!_hasLast)
        {
            _lastPosition = position;
            _hasLast = true;
            return;
        }
        var velocity = (position - _lastPosition) / Mathf.Max((float)delta, 1e-5f);
        _lastPosition = position;
        var teleportSpeedMeters = _teleportSpeed / 3.6f;
        var maxSpeedMeters = _maxSpeed / 3.6f;
        if (velocity.Length() > teleportSpeedMeters) velocity = Vector3.Zero;
        else if (velocity.Length() > maxSpeedMeters) velocity = velocity.Normalized() * maxSpeedMeters;
        _velocity = velocity;
    }

    public bool TryGetInjection(out Vector2 injectionPosition, out Vector3 injectionVelocity,
        out float injectionRadius, out float injectionWeight)
    {
        injectionPosition = Vector2.Zero;
        injectionVelocity = Vector3.Zero;
        injectionRadius = 0.0f;
        injectionWeight = 0.0f;
        var ocean = CrestOceanRendererFacade.Instance;
        if (ocean == null) return false;
        var position = GlobalPosition;
        var xz = new Vector2(position.X, position.Z);
        var relativeVelocity = _velocity;
        relativeVelocity.Y *= _weightUpDownMul;
        CrestCollisionCs.SampleHeightAndVelocity(xz, ocean.CurrentTime, 1.0 / 60.0,
            ocean.OceanLevel, out var waterHeight, out var waterVelocity);
        relativeVelocity.Y -= _compensateForWaveMotion * waterVelocity;

        var interactionWeight = 3.75f * _weight / 5.0f;
        if (_warmUpDuration > 0.0f)
        {
            var t = Mathf.Clamp(_age / _warmUpDuration, 0.0f, 1.0f);
            interactionWeight *= t * t * (3.0f - 2.0f * t);
        }
        var safeRadius = Mathf.Max(_radius, 0.01f);
        var heightAbove = position.Y - waterHeight;
        if (heightAbove < 0.0f)
        {
            var depthRatio = -heightAbove / safeRadius;
            interactionWeight *= Mathf.Exp(-Mathf.Pow(depthRatio * 0.5f, 2.0f));
        }
        else
            interactionWeight *= Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - heightAbove / safeRadius));
        if (interactionWeight < 0.001f) return false;
        injectionPosition = xz;
        injectionVelocity = relativeVelocity;
        injectionRadius = _radius * 1.1f;
        injectionWeight = interactionWeight;
        return true;
    }
}
