using Godot;
using Godot.Collections;

#pragma warning disable CS8604
namespace Crest.Godot;

[Tool, GlobalClass]
public partial class CrestRegisterSeaFloorDepthInput : CrestRegisterLodDataInput
{
    [Export] public int _version { get; set; }
    [Export] public bool _assignOceanDepthMaterial { get; set; } = true;
    [Export] public bool _relative { get; set; }
    public override void _EnterTree() => AddToGroup("crest_depth_input");
    public override void _Ready()
    {
        if (_assignOceanDepthMaterial)
        {
            var shader = GD.Load<Shader>("res://addons/crest/shaders/inputs/ocean_depths.gdshader");
            if (shader != null) MaterialOverride = new ShaderMaterial { Shader = shader };
        }
        base._Ready();
    }
    public override Dictionary GetInjection()
    {
        var source = MaterialTexture("_MainTex");
        var cached = GetInputShaderName().Contains("cache") || source != null;
        var texture = RasterizedInput("depth", cached ? CrestInputRasterizer.ValueMode.Texture :
                CrestInputRasterizer.ValueMode.WorldHeight, source,
            cached ? Colors.White : Colors.Transparent,
            MaterialVector4("_MainTex_ST", new Vector4(1, 1, 0, 0)));
        return new Dictionary
        {
            ["rect_center"] = GetRectCenter(false), ["rect_half_size"] = GetRectHalfSize(),
            ["texture"] = texture, ["height_offset"] = cached && _relative ? GlobalPosition.Y : 0.0f,
            ["sea_level_offset"] = 0.0f, ["mode"] = 0.0f,
        };
    }
}
#pragma warning restore CS8604
