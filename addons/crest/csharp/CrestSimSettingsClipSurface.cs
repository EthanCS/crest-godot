using Godot;

namespace Crest.Godot;

[GlobalClass]
public partial class CrestSimSettingsClipSurface : Resource
{
    [Export] public bool clip_by_default { get; set; } = true;
}
