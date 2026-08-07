@tool
class_name CrestFloatingObjectBase
extends Node3D
## Base class for floating objects (Crest FloatingObjectBase).

## Diameter of the object, for physics purposes. Larger = smoother wave
## response.
@export var object_width := 3.0

var _in_water := false


func in_water() -> bool:
	return _in_water


## Velocity of the object; override in subclasses.
func get_velocity() -> Vector3:
	return Vector3.ZERO


## Finds the RigidBody3D this component drives: itself or a parent.
func get_body() -> RigidBody3D:
	var node: Node = self
	while node:
		if node is RigidBody3D:
			return node
		node = node.get_parent()
	return null
