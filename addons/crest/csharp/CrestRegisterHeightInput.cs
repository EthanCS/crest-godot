using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// Crest RegisterHeightInput. Geometry world Y is written to the sea-level
/// offset (G) channel of the sea-floor-depth simulation.
[Tool, GlobalClass]
public partial class CrestRegisterHeightInput : CrestRegisterLodDataInputWithSplineSupport
{
    [Export] public int _version { get; set; }
    [Export] public CrestRegisterHeightInputDebugFields _debug { get; set; } = new();

    public override void _EnterTree() => AddToGroup("crest_height_input");

    public override Dictionary GetInjection()
    {
        var texture = RasterizedInput("height", CrestInputRasterizer.ValueMode.WorldHeight,
            null, Colors.Transparent);
        return new Dictionary
        {
            ["rect_center"] = GetRectCenter(true), ["rect_half_size"] = GetRectHalfSize(),
            ["texture"] = texture, ["height_offset"] = 0.0f, ["sea_level_offset"] = 0.0f,
            ["mode"] = 2.0f,
        };
    }
}
