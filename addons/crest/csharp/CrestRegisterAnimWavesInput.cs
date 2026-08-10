using Godot;
using Godot.Collections;

namespace Crest.Godot;

[Tool, GlobalClass]
public partial class CrestRegisterAnimWavesInputCs : CrestRegisterLodDataInputCs
{
    [Export] public float amplitude { get; set; } = 1.0f;
    [Export] public bool set_height { get; set; }
    public override void _EnterTree() => AddToGroup("crest_anim_waves_input");
    public override Dictionary GetInjection() => new()
    {
        ["rect_center"] = GetRectCenter(), ["radius"] = GetRectHalfSize().X,
        ["amplitude"] = amplitude, ["blend_mode"] = set_height ? 1.0f : 0.0f,
    };
}
