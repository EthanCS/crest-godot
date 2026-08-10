using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# foam injection input. Dictionary keys mirror the foam compute shader contract.
[Tool, GlobalClass]
public partial class CrestRegisterFoamInput : CrestRegisterLodDataInputWithSplineSupport
{
    [Export] public int _version { get; set; }
    [Export] public bool _followHorizontalMotion { get; set; }
    public override void _EnterTree() => AddToGroup("crest_foam_input");

    public override Dictionary GetInjection()
    {
        var shader = GetInputShaderName();
        var overrideFoam = shader.Contains("override");
        var source = MaterialTexture("_MainTex");
        var rasterMode = overrideFoam ? CrestInputRasterizer.ValueMode.Coverage :
            shader.Contains("vert_col") ? CrestInputRasterizer.ValueMode.VertexRed : CrestInputRasterizer.ValueMode.Texture;
        var texture = RasterizedInput("foam", rasterMode, source, Colors.White,
            MaterialVector4("_MainTex_ST", new Vector4(1, 1, 0, 0)));
        var injection = new Dictionary
        {
            ["rect_center"] = GetRectCenter(_followHorizontalMotion),
            ["rect_half_size"] = GetRectHalfSize(),
            ["strength"] = overrideFoam ? MaterialFloat("_FoamValue") : MaterialFloat("_Strength", 1.0f),
            ["mode"] = overrideFoam ? 1.0f : 0.0f,
        };
#pragma warning disable CS8604
        injection["texture"] = texture;
#pragma warning restore CS8604
        return injection;
    }
}
