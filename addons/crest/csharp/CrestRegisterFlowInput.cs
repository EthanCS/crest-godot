using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# flow-map/fixed-velocity input matching RegisterFlowInput.
[Tool, GlobalClass]
public partial class CrestRegisterFlowInputCs : CrestRegisterLodDataInputCs
{
    [Export] public Texture2D? flow_map { get; set; }
    [Export] public float strength { get; set; } = 1.0f;
    [Export] public bool fixed_direction { get; set; }
    [Export] public float speed { get; set; } = 1.0f;
    [Export] public float direction_degrees { get; set; }

    public override void _EnterTree() => AddToGroup("crest_flow_input");

    public override Dictionary GetInjection()
    {
        var radians = Mathf.DegToRad(direction_degrees);
        var direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        var injection = new Dictionary
        {
            ["rect_center"] = GetRectCenter(),
            ["rect_half_size"] = GetRectHalfSize(),
            ["fixed_velocity"] = direction * speed,
            ["strength"] = strength,
            ["mode"] = fixed_direction ? 1.0f : 0.0f,
        };
#pragma warning disable CS8604
        injection["texture"] = fixed_direction ? Variant.From((GodotObject)null!) : flow_map;
#pragma warning restore CS8604
        return injection;
    }
}
