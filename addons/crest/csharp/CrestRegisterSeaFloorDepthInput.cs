using Godot;
using Godot.Collections;

#pragma warning disable CS8604
namespace Crest.Godot;

[Tool, GlobalClass]
public partial class CrestRegisterSeaFloorDepthInputCs : CrestRegisterLodDataInputCs
{
    [Export] public Texture2D? height_texture { get; set; }
    [Export] public float height_offset { get; set; }
    public override void _EnterTree() => AddToGroup("crest_depth_input");
    public override Dictionary GetInjection() => new()
    {
        ["rect_center"] = GetRectCenter(), ["rect_half_size"] = GetRectHalfSize(),
        ["texture"] = height_texture, ["height_offset"] = GlobalPosition.Y + height_offset,
        ["sea_level_offset"] = 0.0f, ["mode"] = 0.0f,
    };
}
#pragma warning restore CS8604
