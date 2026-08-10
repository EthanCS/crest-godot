using Godot;

namespace Crest.Godot;

[GlobalClass]
public partial class CrestSimSettingsShadow : Resource
{
    [Export] public float jitter_diameter_soft { get; set; } = 15.0f;
    [Export] public float current_frame_weight_soft { get; set; } = 0.03f;
    [Export] public float jitter_diameter_hard { get; set; } = 0.6f;
    [Export] public float current_frame_weight_hard { get; set; } = 0.15f;
}
