using Godot;

namespace Crest.Godot;

[GlobalClass]
public partial class CrestSimSettingsFoam : Resource
{
    [Export] public int _version { get; set; }
    [Export] public bool _prewarm { get; set; } = true;
    [Export(PropertyHint.Range, "0,20,0.01")] public float _foamFadeRate { get; set; } = 0.8f;
    [Export(PropertyHint.Range, "0,5,0.01")] public float _waveFoamStrength { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _waveFoamCoverage { get; set; } = 0.55f;
    [Export(PropertyHint.Range, "0,13,1")] public int _filterWaves { get; set; }
    [Export(PropertyHint.Range, "0.01,3,0.01")] public float _shorelineFoamMaxDepth { get; set; } = 0.65f;
    [Export(PropertyHint.Range, "0,5,0.01")] public float _shorelineFoamStrength { get; set; } = 2.0f;
    [Export] public int _renderTextureGraphicsFormat { get; set; } = 45;
    [Export(PropertyHint.Range, "15,200,1")] public float _simulationFrequency { get; set; } = 30.0f;
}
