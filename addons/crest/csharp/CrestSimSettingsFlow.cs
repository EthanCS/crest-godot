using Godot;

namespace Crest.Godot;

[GlobalClass]
public partial class CrestSimSettingsFlow : Resource
{
    [Export] public int _version { get; set; }
}
