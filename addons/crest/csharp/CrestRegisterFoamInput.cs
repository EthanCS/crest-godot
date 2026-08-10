using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# foam injection input. Dictionary keys mirror the foam compute shader contract.
[Tool, GlobalClass]
public partial class CrestRegisterFoamInputCs : CrestRegisterLodDataInputCs
{
    [Export] public float strength { get; set; } = 1.0f;
    [Export] public Texture2D? texture { get; set; }
    [Export] public bool sphere_mode { get; set; }

    public override void _EnterTree() => AddToGroup("crest_foam_input");

    public override Dictionary GetInjection()
    {
        var injection = new Dictionary
        {
            ["rect_center"] = GetRectCenter(),
            ["rect_half_size"] = GetRectHalfSize(),
            ["strength"] = strength,
            ["mode"] = sphere_mode ? 1.0f : 0.0f,
        };
#pragma warning disable CS8604
        injection["texture"] = sphere_mode ? Variant.From((GodotObject)null!) : texture;
#pragma warning restore CS8604
        return injection;
    }
}
