@tool
class_name CrestRegisterShadowInput
extends Node3D
## Registers an analytic sphere occluder for the shadow sim. Port of Crest's
## RegisterShadowInput: the sim darkens the water where the main light is
## blocked by the sphere (soft shadows via jittered accumulation).

## Occluder radius in metres.
@export var radius := 1.0


func _enter_tree() -> void:
	add_to_group(&"crest_shadow_input")


## Collected every frame by CrestOceanRenderer while the shadow sim runs.
func get_shadow_caster() -> Dictionary:
	return {
		"pos": global_position,
		"radius": radius,
	}
