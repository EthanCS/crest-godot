using Godot;
using Godot.Collections;

namespace Crest.Godot;

[Tool, GlobalClass]
public partial class CrestRegisterShadowInput : CrestRegisterLodDataInput
{
    [Export] public int _version { get; set; }
    public override void _EnterTree() => AddToGroup("crest_shadow_input");
    public Dictionary GetShadowCaster() => new()
    {
        ["rect_center"] = GetRectCenter(false), ["rect_half_size"] = GetRectHalfSize(),
        ["texture"] = RasterizedInput("shadow", CrestInputRasterizer.ValueMode.Coverage),
        ["shadow_value"] = MaterialFloat("_ShadowValue", 1.0f),
    };
    public override Dictionary GetInjection() => GetShadowCaster();
}
