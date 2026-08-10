using Godot;

namespace Crest.Godot;

[Tool, GlobalClass]
public partial class CrestSimSettingsWave : Resource
{
    [Export] public float simulation_frequency { get; set; } = 60.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float damping { get; set; } = 0.05f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float courant_number { get; set; } = 0.7f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float attenuation_in_shallows { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,32,0.1")] public float horiz_displace { get; set; } = 3.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float displace_clamp { get; set; } = 0.3f;
    [Export] public float gravity_multiplier { get; set; } = 1.0f;
}
