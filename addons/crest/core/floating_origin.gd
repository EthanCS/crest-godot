@tool
class_name CrestFloatingOrigin
extends Node3D
## Keeps the viewer near the world origin by shifting the whole scene root
## when the viewer travels past a threshold (Crest's FloatingOrigin /
## ShiftingOrigin, simplified).
##
## The ocean cascades re-snap automatically since they follow the viewer;
## physics objects are moved along with everything else.

## Distance from the origin that triggers a shift.
@export var threshold := 4096.0

## Node whose position is watched. Defaults to the active camera.
@export var viewpoint: Node3D

## Fired after the scene has been shifted, with the applied world offset.
signal origin_shifted(offset: Vector3)


func _physics_process(_delta: float) -> void:
	if Engine.is_editor_hint():
		return
	var viewer := _get_viewer()
	if viewer == null:
		return
	var pos := viewer.global_position
	if maxf(absf(pos.x), absf(pos.z)) < threshold:
		return
	# Shift on the XZ plane only (the ocean is height-agnostic).
	var offset := Vector3(pos.x, 0.0, pos.z)
	var root := get_tree().current_scene
	if root == null:
		return
	_shift_recursive(root, offset)
	origin_shifted.emit(offset)


func _shift_recursive(node: Node, offset: Vector3) -> void:
	# Move only top-level nodes of the scene root (children follow).
	for child in node.get_children():
		if child is Node3D and not child is CrestFloatingOrigin:
			child.global_position -= offset


func _get_viewer() -> Node3D:
	if viewpoint:
		return viewpoint
	var cam := get_viewport().get_camera_3d()
	if cam:
		return cam
	return null
