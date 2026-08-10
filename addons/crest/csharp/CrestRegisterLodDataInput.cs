using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# port of Crest's RegisterLodDataInput base. Inputs expose a world-space
/// XZ rectangle and a simulation-specific injection dictionary.
[Tool, GlobalClass]
public partial class CrestRegisterLodDataInputCs : Node3D
{
    [Export] public Vector2 rect_size { get; set; } = new(10.0f, 10.0f);
    [Export] public bool follow_ocean { get; set; }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint() || !follow_ocean)
            return;
        var xz = new Vector2(GlobalPosition.X, GlobalPosition.Z);
        var time = Time.GetTicksMsec() / 1000.0;
        if (CrestCollisionCs.SampleHeight(xz, time, 0.0f, out var height))
        {
            var position = GlobalPosition;
            position.Y = (float)height;
            GlobalPosition = position;
        }
    }

    public Vector2 GetRectCenter() => new(GlobalPosition.X, GlobalPosition.Z);

    public Vector2 GetRectHalfSize()
    {
        var scale = GlobalTransform.Basis.Scale;
        var extents = new Vector2(Mathf.Abs(rect_size.X * scale.X), Mathf.Abs(rect_size.Y * scale.Z));
        return extents * 0.5f;
    }

    public virtual Dictionary GetInjection() => new();
}
