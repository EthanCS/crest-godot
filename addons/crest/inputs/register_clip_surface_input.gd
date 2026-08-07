@tool
class_name CrestRegisterClipSurfaceInput
extends CrestRegisterLodDataInput
## Removes (or restores) the ocean surface inside the input rect, e.g. to
## cut water out of boat hulls or around docks. Port of Crest's
## RegisterClipSurfaceInput.

## true: clip (remove) the surface; false: un-clip (restore) it.
@export var mode_clip := true
## Coverage mask (R channel) stretched across the rect; null = full rect.
@export var mask_texture: Texture2D


func _enter_tree() -> void:
	add_to_group(&"crest_clip_input")


func get_injection() -> Dictionary:
	return {
		"rect_center": get_rect_center(),
		"rect_half_size": get_rect_half_size(),
		"mode": 0.0 if mode_clip else 1.0,
		"texture": mask_texture,
	}
