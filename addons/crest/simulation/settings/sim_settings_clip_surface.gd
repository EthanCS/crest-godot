@tool
class_name CrestSimSettingsClipSurface
extends Resource
## Port of Crest's SimSettingsClipSurface.

## When enabled the clip surface defaults to "clipped" everywhere and inputs
## un-clip; otherwise the ocean renders everywhere and inputs clip.
@export var clip_by_default := true
