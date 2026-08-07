@tool
class_name CrestRegisterSeaFloorDepthInput
extends CrestRegisterLodDataInput
## Feeds a static terrain heightfield into the sea floor depth sim (max
## blended against the depth baseline). Port of Crest's
## RegisterSeaFloorDepthInput. For a rendered terrain cache use
## CrestOceanDepthCache instead.

## Height texture; R channel = terrain height relative to this node's Y,
## stretched across the input rect (world height = R + node Y +
## height_offset).
@export var height_texture: Texture2D
## Extra world-space height offset added on top of the node Y.
@export var height_offset := 0.0


func _enter_tree() -> void:
	add_to_group(&"crest_depth_input")


func get_injection() -> Dictionary:
	return {
		"rect_center": get_rect_center(),
		"rect_half_size": get_rect_half_size(),
		"texture": height_texture,
		"height_offset": global_position.y + height_offset,
		"sea_level_offset": 0.0,
		"mode": 0.0,
	}
