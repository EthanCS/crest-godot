using Godot;
using Godot.Collections;

namespace Crest.Godot;

[Tool, GlobalClass]
public partial class CrestRegisterAnimWavesInput : CrestRegisterLodDataInputWithSplineSupport
{
    [Export] public int _version { get; set; }
    [Export] public bool _filterByWavelength { get; set; }
    [Export] public float _octaveWavelength { get; set; }
    [Export] public bool _renderAfterDynamicWaves { get; set; } = true;
    [Export] public bool _followHorizontalMotion { get; set; } = true;
    [Export] public float _maxDisplacementVertical { get; set; }
    [Export] public float _maxDisplacementHorizontal { get; set; }
    [Export] public bool _reportRendererBoundsToOceanSystem { get; set; }
    public override void _EnterTree() => AddToGroup("crest_anim_waves_input");
    public override Dictionary GetInjection()
    {
        var shader = GetInputShaderName();
        var setHeight = shader.Contains("set_height");
        var source = MaterialTexture("_MainTex");
        var texture = RasterizedInput("animated", setHeight
                ? CrestInputRasterizer.ValueMode.WorldHeight : CrestInputRasterizer.ValueMode.Texture,
            source, setHeight ? Colors.Transparent : Colors.Black,
            MaterialVector4("_MainTex_ST", new Vector4(1, 1, 0, 0)));
        var injection = new Dictionary
        {
            ["rect_center"] = GetRectCenter(_followHorizontalMotion), ["rect_half_size"] = GetRectHalfSize(),
            ["amplitude"] = MaterialFloat("_Strength", 1.0f),
            ["blend_mode"] = setHeight ? 1.0f : 0.0f,
            ["heights_only"] = MaterialFloat("_HeightsOnly", 1.0f),
            ["wavelength"] = _filterByWavelength ? _octaveWavelength : _renderAfterDynamicWaves ? 0.0f : -1.0f,
        };
#pragma warning disable CS8604
        injection["texture"] = texture;
#pragma warning restore CS8604
        return injection;
    }
}
