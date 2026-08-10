using Godot;

namespace Crest.Godot;

[GlobalClass]
public partial class CrestSimSettingsAnimatedWaves : Resource
{
    [Export] public float attenuation_in_shallows { get; set; } = 0.95f;
    [Export] public float maximum_attenuation_depth { get; set; } = 1000.0f;
    [Export] public int collision_source { get; set; }
}
