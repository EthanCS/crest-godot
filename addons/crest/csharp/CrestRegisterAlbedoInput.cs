using Godot;
using Godot.Collections;

#pragma warning disable CS8604
namespace Crest.Godot;

[Tool, GlobalClass]
public partial class CrestRegisterAlbedoInput : CrestRegisterLodDataInput
{
    [Export] public int _version { get; set; }
    [Export] public bool _followHorizontalMotion { get; set; }
    public override void _EnterTree() => AddToGroup("crest_albedo_input");
    public override Dictionary GetInjection()
    {
        var texture = RasterizedInput("albedo", CrestInputRasterizer.ValueMode.Albedo,
            MaterialTexture("_Texture"), Colors.White,
            MaterialVector4("_Texture_ST", new Vector4(1, 1, 0, 0)), MaterialFloat("_Cutoff", 0.5f));
        return new Dictionary
        {
            ["rect_center"] = GetRectCenter(_followHorizontalMotion), ["rect_half_size"] = GetRectHalfSize(),
            ["tint"] = MaterialColor("_Color", Colors.White), ["texture"] = texture,
            ["blend_source"] = MaterialFloat("_BlendModeSource", 5.0f),
            ["blend_target"] = MaterialFloat("_BlendModeTarget", 10.0f),
        };
    }
}
#pragma warning restore CS8604
