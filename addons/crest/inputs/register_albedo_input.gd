@tool
class_name CrestRegisterAlbedoInput
extends CrestRegisterLodDataInput
## Overrides the water scattering colour inside the input rect (tint *
## texture). Port of Crest's RegisterAlbedoInput.

@export var tint := Color.WHITE
## Colour texture stretched across the rect; null = uniform tint.
@export var texture: Texture2D


func _enter_tree() -> void:
	add_to_group(&"crest_albedo_input")


func get_injection() -> Dictionary:
	return {
		"rect_center": get_rect_center(),
		"rect_half_size": get_rect_half_size(),
		"tint": tint,
		"texture": texture,
	}
