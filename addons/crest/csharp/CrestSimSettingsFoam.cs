using Godot;

namespace Crest.Godot;

[GlobalClass]
public partial class CrestSimSettingsFoam : Resource
{
    [Export] public float foam_fade_rate { get; set; } = 0.8f;
    [Export] public float wave_foam_strength { get; set; } = 1.0f;
    [Export] public float wave_foam_coverage { get; set; } = 0.55f;
    [Export] public int filter_waves { get; set; }
    [Export] public float shoreline_foam_max_depth { get; set; } = 0.65f;
    [Export] public float shoreline_foam_strength { get; set; } = 2.0f;
    [Export] public float simulation_frequency { get; set; } = 30.0f;
    [Export] public bool prewarm { get; set; } = true;
}
