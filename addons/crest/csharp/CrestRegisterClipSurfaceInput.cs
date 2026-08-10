using Godot;
using Godot.Collections;

#pragma warning disable CS8604
namespace Crest.Godot;

[Tool, GlobalClass]
public partial class CrestRegisterClipSurfaceInputCs : CrestRegisterLodDataInputCs
{
    [Export] public bool mode_clip { get; set; } = true;
    [Export] public Texture2D? mask_texture { get; set; }
    public override void _EnterTree() => AddToGroup("crest_clip_input");
    public override Dictionary GetInjection() => new()
    {
        ["rect_center"] = GetRectCenter(), ["rect_half_size"] = GetRectHalfSize(),
        ["mode"] = mode_clip ? 0.0f : 1.0f, ["texture"] = mask_texture,
    };
}
#pragma warning restore CS8604
