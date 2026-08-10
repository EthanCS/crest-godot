using Godot;

namespace Crest.Godot;

[Tool, GlobalClass]
public partial class CrestFloatingObjectBase : Node3D
{
    public float ObjectWidth { get; set; } = 3.0f;
    protected bool InWaterState { get; set; }

    public virtual bool in_water() => InWaterState;
    public virtual Vector3 get_velocity() => Vector3.Zero;

    protected RigidBody3D? GetBody()
    {
        for (Node? node = this; node != null; node = node.GetParent())
            if (node is RigidBody3D body) return body;
        return null;
    }
}
