using Godot;

namespace Crest.Godot;

[GlobalClass]
public partial class CrestSimSettingsAnimatedWaves : Resource
{
    [Export] public int _version { get; set; }
    [Export(PropertyHint.Range, "1,4,0.01")] public float _waveResolutionMultiplier { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float _attenuationInShallows { get; set; } = 0.95f;
    [Export(PropertyHint.Range, "1,1000,1")] public float _shallowsMaxDepth { get; set; } = 1000.0f;
    [Export] public int _collisionSource { get; set; } = 2;
    [Export] public int _maxQueryCount { get; set; } = 4096;
    [Export] public bool _pingPongCombinePass { get; set; } = true;
    [Export] public int _renderTextureGraphicsFormat { get; set; } = 48;
}
