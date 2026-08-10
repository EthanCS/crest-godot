using Godot;

namespace Crest.Godot;

[GlobalClass]
public partial class CrestSimSettingsShadow : Resource
{
    [Export] public int _version { get; set; }
    [Export(PropertyHint.Range, "0,32,0.01")] public float _jitterDiameterSoft { get; set; } = 15.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _currentFrameWeightSoft { get; set; } = 0.03f;
    [Export(PropertyHint.Range, "0,32,0.01")] public float _jitterDiameterHard { get; set; } = 0.6f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _currentFrameWeightHard { get; set; } = 0.15f;
    [Export] public bool _allowNullLight { get; set; }
    [Export] public bool _allowNoShadows { get; set; }
}
