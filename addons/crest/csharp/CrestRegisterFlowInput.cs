using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# flow-map/fixed-velocity input matching RegisterFlowInput.
[Tool, GlobalClass]
public partial class CrestRegisterFlowInput : CrestRegisterLodDataInputWithSplineSupport
{
    [Export] public int _version { get; set; }
    [Export] public bool _followHorizontalMotion { get; set; }
    public override void _EnterTree() => AddToGroup("crest_flow_input");

    public override Dictionary GetInjection()
    {
        var fixedDirection = GetInputShaderName().Contains("fixed_direction");
        var radians = MaterialFloat("_Direction") * Mathf.Tau;
        var direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        var texture = RasterizedInput("flow", fixedDirection ? CrestInputRasterizer.ValueMode.Coverage :
                CrestInputRasterizer.ValueMode.Texture, MaterialTexture("_FlowMap"), Colors.White,
            MaterialVector4("_FlowMap_ST", new Vector4(1, 1, 0, 0)));
        var injection = new Dictionary
        {
            ["rect_center"] = GetRectCenter(_followHorizontalMotion),
            ["rect_half_size"] = GetRectHalfSize(),
            ["fixed_velocity"] = direction * MaterialFloat("_Speed", 1.0f),
            ["strength"] = MaterialFloat("_Strength", 1.0f),
            ["mode"] = fixedDirection ? 1.0f : 0.0f,
            ["flip_x"] = MaterialFloat("_FlipX"), ["flip_z"] = MaterialFloat("_FlipZ"),
            ["feather_enabled"] = MaterialFloat("_FeatherAtUVExtents"),
            ["feather_width"] = MaterialFloat("_FeatherWidth", 0.1f),
        };
#pragma warning disable CS8604
        injection["texture"] = texture;
#pragma warning restore CS8604
        return injection;
    }
}
