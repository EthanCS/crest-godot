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
    [Export] public float radius { get; set; } = 1.0f;
    [Export] public float weight { get; set; } = 1.0f;
    [Export] public float weight_up_down_mul { get; set; } = 0.5f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float compensate_for_wave_motion { get; set; } = 0.45f;
    [Export] public float foam_strength { get; set; } = 0.5f;
    [Export] public float teleport_speed { get; set; } = 138.9f;
    [Export] public float max_speed { get; set; } = 27.8f;

    private Vector3 _lastPosition;
    private bool _hasLast;
    private Vector3 _velocity;

    public override void _EnterTree()
    {
        if (!Active.Contains(this)) Active.Add(this);
        AddToGroup("crest_sphere_interaction_cs");
    }

    public override void _ExitTree() => Active.Remove(this);

    public override void _PhysicsProcess(double delta)
    {
        if (Engine.IsEditorHint()) return;
        var position = GlobalPosition;
        if (!_hasLast)
        {
            _lastPosition = position;
            _hasLast = true;
            return;
        }
        var velocity = (position - _lastPosition) / Mathf.Max((float)delta, 1e-5f);
        _lastPosition = position;
        if (velocity.Length() > teleport_speed) velocity = Vector3.Zero;
        else if (velocity.Length() > max_speed) velocity = velocity.Normalized() * max_speed;
        _velocity = velocity;
    }

    public Dictionary get_sphere_injection()
    {
        if (!TryGetInjection(out var position, out var velocity, out var injectionRadius,
            out var injectionWeight)) return new Dictionary();
        return new Dictionary
        {
            ["pos"] = position,
            ["vel"] = velocity,
            ["radius"] = injectionRadius,
            ["weight"] = injectionWeight,
            ["foam"] = foam_strength * injectionWeight,
        };
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
        relativeVelocity.Y *= weight_up_down_mul;
        CrestCollisionCs.SampleHeightAndVelocity(xz, ocean.CurrentTime, 1.0 / 60.0,
            ocean.OceanLevel, out var waterHeight, out var waterVelocity);
        relativeVelocity.Y -= compensate_for_wave_motion * waterVelocity;

        var interactionWeight = 3.75f * weight / 5.0f;
        var safeRadius = Mathf.Max(radius, 0.01f);
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
        injectionRadius = radius * 1.1f;
        injectionWeight = interactionWeight;
        return true;
    }
}
