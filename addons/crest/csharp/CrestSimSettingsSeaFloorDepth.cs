using Godot;

namespace Crest.Godot;

[GlobalClass]
public partial class CrestSimSettingsSeaFloorDepth : Resource
{
    [Export] public bool _allowVaryingWaterLevel { get; set; } = true;
}
