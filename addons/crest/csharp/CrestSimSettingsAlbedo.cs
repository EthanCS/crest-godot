using Godot;

namespace Crest.Godot;

[GlobalClass]
public partial class CrestSimSettingsAlbedo : Resource
{
    [Export] public int _version { get; set; }
    [Export] public int _resolution { get; set; } = 768;
}
