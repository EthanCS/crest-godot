@tool
class_name CrestRegisterAnimWavesInput
extends CrestRegisterLodDataInput
## Adds a local displacement bump to the animated waves (before the combine
## pass). Port of Crest's RegisterAnimWavesInput with the AddBump material.

## Height of the bump (m). Negative pushes down.
@export var amplitude := 1.0
## Pull the surface towards [member amplitude] instead of adding to it.
@export var set_height := false


func _enter_tree() -> void:
	add_to_group(&"crest_anim_waves_input")


func get_injection() -> Dictionary:
	return {
		"rect_center": get_rect_center(),
		"radius": get_rect_half_size().x,
		"amplitude": amplitude,
		"blend_mode": 1.0 if set_height else 0.0,
	}
