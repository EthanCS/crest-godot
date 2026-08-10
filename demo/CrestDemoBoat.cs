using Crest.Godot;
using Godot;

public partial class CrestDemoBoat : Node3D
{
    [Export] public float radius { get; set; } = 12.0f;
    [Export] public float speed { get; set; } = 4.0f;
    [Export] public float draft { get; set; } = 0.45f;
    [Export] public float follow_smoothing { get; set; } = 6.0f;
    [Export] public Vector2 center { get; set; } = Vector2.Zero;

    private float _angle;
    private float _smoothY;

    public override void _Ready() => _smoothY = GlobalPosition.Y;

    public override void _PhysicsProcess(double delta)
    {
        var ocean = CrestOceanRendererFacade.Instance;
        if (ocean == null) return;
        _angle += speed / Mathf.Max(radius, 0.001f) * (float)delta;
        var xz = center + new Vector2(Mathf.Cos(_angle), Mathf.Sin(_angle)) * radius;
        CrestCollisionCs.SampleHeight(xz, ocean.CurrentTime, ocean.OceanLevel, out var waterY);
        var blend = 1.0f - Mathf.Exp(-follow_smoothing * (float)delta);
        _smoothY = Mathf.Lerp(_smoothY, waterY + draft, blend);
        GlobalPosition = new Vector3(xz.X, _smoothY, xz.Y);

        var heading = new Vector3(-Mathf.Sin(_angle), 0.0f, Mathf.Cos(_angle));
        var normal = CrestCollisionCs.SampleNormal(xz, ocean.CurrentTime, 1.5f);
        var right = normal.Cross(heading).Normalized();
        var forward = right.Cross(normal).Normalized();
        GlobalBasis = new Basis(forward, normal, -right).Orthonormalized();
    }
}
