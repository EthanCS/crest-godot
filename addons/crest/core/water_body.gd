@tool
class_name CrestWaterBody
extends Node3D
## Defines a water body volume. The ocean surface renders one tile per water
## body when using the "per water body" mode; the collision/query API can be
## scoped to a body. Mirrors Crest's WaterBody.

## If true the AABB is recomputed from child meshes on ready.
@export var auto_calculate_bounds := false

## World-space bounds of this water body on the XZ plane.
@export var bounds := AABB(Vector3(-50, -1, -50), Vector3(100, 2, 100))


static var _bodies: Array[CrestWaterBody] = []


func _enter_tree() -> void:
	_bodies.append(self)


func _exit_tree() -> void:
	_bodies.erase(self)


static func get_bodies() -> Array[CrestWaterBody]:
	return _bodies


func contains_xz(point: Vector3) -> bool:
	return bounds.has_point(point)
