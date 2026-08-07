@tool
class_name CrestRegisterLodDataInput
extends Node3D
## Base class for ocean input nodes: describes the world-space XZ rect the
## input covers and optionally follows the water surface vertically. Port of
## Crest's RegisterLodDataInput (rect + texture variant).
##
## Subclasses add the node to their sim's input group in _enter_tree() and
## override get_injection() to return the Dictionary consumed by the inject
## compute shaders (see addons/crest/shaders/sim/inject_*.glsl).
## CrestOceanRenderer collects the inputs every frame.

## World XZ size of the rect the input covers (before node scale).
@export var rect_size := Vector2(10.0, 10.0)
## Snap the node's Y to the water surface every frame (analytic Gerstner
## sampling; does nothing until an ocean exists).
@export var follow_ocean := false


func _process(_delta: float) -> void:
	if Engine.is_editor_hint() or not follow_ocean:
		return
	var h := [0.0]
	if CrestCollision.sample_height(Vector2(global_position.x, global_position.z), h):
		global_position.y = h[0]


## World XZ position of the rect centre.
func get_rect_center() -> Vector2:
	return Vector2(global_position.x, global_position.z)


## World XZ half extents of the rect, including the node's XZ scale.
func get_rect_half_size() -> Vector2:
	var s := global_transform.basis.get_scale()
	return Vector2(absf(rect_size.x * s.x), absf(rect_size.y * s.z)) * 0.5


## Override in subclasses. Called every frame by CrestOceanRenderer while
## the matching simulation is enabled.
func get_injection() -> Dictionary:
	return {}
