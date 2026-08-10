using Godot;

namespace Crest.Godot;

[GlobalClass]
public partial class CrestSimSettingsClipSurface : Resource
{
    [Export] public int _version { get; set; }
    [Export] public int _renderTextureGraphicsFormat { get; set; } = 21;
}
