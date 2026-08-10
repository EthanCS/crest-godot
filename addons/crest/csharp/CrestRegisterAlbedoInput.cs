using Godot;
using Godot.Collections;

#pragma warning disable CS8604
namespace Crest.Godot;

[Tool, GlobalClass]
public partial class CrestRegisterAlbedoInputCs : CrestRegisterLodDataInputCs
{
    [Export] public Color tint { get; set; } = Colors.White;
    [Export] public Texture2D? texture { get; set; }
    public override void _EnterTree() => AddToGroup("crest_albedo_input");
    public override Dictionary GetInjection() => new()
    {
        ["rect_center"] = GetRectCenter(), ["rect_half_size"] = GetRectHalfSize(),
        ["tint"] = tint, ["texture"] = texture,
    };
}
#pragma warning restore CS8604
