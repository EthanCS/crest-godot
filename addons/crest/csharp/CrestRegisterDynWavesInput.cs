using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// Official general Renderer input for the Dynamic Waves simulation.
[Tool, GlobalClass]
public partial class CrestRegisterDynWavesInput : CrestRegisterLodDataInput
{
    [Export] public int _version { get; set; }
    public override void _EnterTree() => AddToGroup("crest_dyn_waves_input");

    public override Dictionary GetInjection()
    {
        // Material shader parameters remain Material-owned. The renderer path
        // currently supports Add Bump; object/sphere interaction components
        // provide velocity separately.
        var radius = MaterialFloat("_Radius", 3.0f);
        return new Dictionary
        {
            ["rect_center"] = GetRectCenter(false), ["rect_half_size"] = new Vector2(radius, radius),
            ["amplitude"] = MaterialFloat("_Amplitude", 1.0f),
        };
    }
}
