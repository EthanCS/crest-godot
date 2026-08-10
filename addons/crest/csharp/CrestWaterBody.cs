using Godot;
using System.Collections.Generic;

namespace Crest.Godot;

/// C# water-body volume and registry, matching Crest's body-scoping API.
[Tool, GlobalClass]
public partial class CrestWaterBodyCs : Node3D
{
    [Export] public bool auto_calculate_bounds { get; set; }
    [Export] public Aabb bounds { get; set; } = new(new Vector3(-50, -1, -50), new Vector3(100, 2, 100));

    private static readonly List<CrestWaterBodyCs> Bodies = new();
    public static IReadOnlyList<CrestWaterBodyCs> GetBodies() => Bodies;

    public override void _EnterTree()
    {
        if (!Bodies.Contains(this)) Bodies.Add(this);
    }

    public override void _ExitTree() => Bodies.Remove(this);

    public bool ContainsXz(Vector3 point) => bounds.HasPoint(point);
}
