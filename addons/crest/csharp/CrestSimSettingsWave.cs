using Godot;

namespace Crest.Godot;

[Tool, GlobalClass]
public partial class CrestSimSettingsWave : Resource
{
    [Export] public int _version { get; set; }
    [Export(PropertyHint.Range, "0,32,0.1")] public float _minGridSize { get; set; }
    [Export(PropertyHint.Range, "0,32,0.1")] public float _maxGridSize { get; set; }
    [Export(PropertyHint.Range, "15,200,1")] public float _simulationFrequency { get; set; } = 60.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _damping { get; set; } = 0.05f;
    [Export(PropertyHint.Range, "0.1,1,0.01")] public float _courantNumber { get; set; } = 0.7f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _attenuationInShallows { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,20,0.1")] public float _horizDisplace { get; set; } = 3.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _displaceClamp { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "0,64,0.1")] public float _gravityMultiplier { get; set; } = 1.0f;
}
