using Godot;

namespace Crest.Godot;

/// Serialized counterpart of Crest's RegisterLodDataInputWithSplineSupport.
/// These fields intentionally do not live on the non-spline input base.
[Tool, GlobalClass]
public partial class CrestRegisterLodDataInputWithSplineSupport : CrestRegisterLodDataInput
{
    [Export] public float _featherWidth { get; set; }
    [Export] public bool _overrideSplineSettings { get; set; }
    [Export] public float _radius { get; set; } = 20.0f;
    [Export] public int _subdivisions { get; set; } = 1;
}
