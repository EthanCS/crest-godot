using Godot;
using Godot.Collections;

namespace Crest.Godot;

[Tool, GlobalClass]
public partial class CrestRegisterShadowInputCs : Node3D
{
    [Export] public float radius { get; set; } = 1.0f;
    public override void _EnterTree() => AddToGroup("crest_shadow_input");
    public Dictionary GetShadowCaster() => new()
    {
        ["pos"] = GlobalPosition, ["radius"] = radius,
    };
}
