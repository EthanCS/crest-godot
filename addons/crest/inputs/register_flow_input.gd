@tool
class_name CrestRegisterFlowInput
extends CrestRegisterLodDataInput
## Injects horizontal water flow into the flow sim inside the input rect.
## Port of Crest's RegisterFlowInput. Two modes (Crest: FlowAddFlowMap /
## FlowFixedDirection):
## - Flow map: adds (tex.rg - 0.5) * strength to the flow.
## - Fixed direction: replaces the flow with a constant velocity.

## Flow map texture; RG encodes direction (0.5 = neutral), stretched across
## the input rect. Used when fixed_direction is false.
@export var flow_map: Texture2D
## Flow map strength multiplier.
@export var strength := 1.0
## true: replace the flow with a fixed velocity instead of adding a flow
## map.
@export var fixed_direction := false
## Speed in m/s for fixed direction mode.
@export var speed := 1.0
## Flow direction in degrees for fixed direction mode (0 = +X, 90 = +Z).
@export var direction_degrees := 0.0


func _enter_tree() -> void:
	add_to_group(&"crest_flow_input")


func get_injection() -> Dictionary:
	var dir := Vector2(cos(deg_to_rad(direction_degrees)), sin(deg_to_rad(direction_degrees)))
	return {
		"rect_center": get_rect_center(),
		"rect_half_size": get_rect_half_size(),
		"fixed_velocity": dir * speed,
		"strength": strength,
		"mode": 1.0 if fixed_direction else 0.0,
		"texture": null if fixed_direction else flow_map,
	}
