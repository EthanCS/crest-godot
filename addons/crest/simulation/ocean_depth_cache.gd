@tool
class_name CrestOceanDepthCache
extends Node3D
## Renders terrain height from a top-down orthographic camera into a cache
## texture that feeds the sea floor depth sim (Crest OceanDepthCache).
## Full implementation pending.

func _enter_tree() -> void:
	add_to_group(&"crest_depth_input")


func get_injection() -> Dictionary:
	return {}
