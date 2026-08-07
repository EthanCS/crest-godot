class_name CrestSampleHeightHelper
extends RefCounted
## Convenience helper mirroring Crest's SampleHeightHelper: caches the
## sampled height and vertical velocity for the last call.

var height := 0.0
var velocity := 0.0


## Samples the water height at world_pos (XZ used). Returns false when no
## ocean is available.
func sample(world_pos: Vector3) -> bool:
	var out := [0.0]
	if not CrestCollision.sample_height(Vector2(world_pos.x, world_pos.z), out):
		return false
	height = out[0]
	return true


## Samples height and vertical velocity (finite difference against dt
## seconds ago).
func sample_height_and_velocity(world_pos: Vector3, dt := 0.0167) -> bool:
	var h := [0.0]
	var v := [0.0]
	if not CrestCollision.sample_height_and_velocity(Vector2(world_pos.x, world_pos.z), dt, h, v):
		return false
	height = h[0]
	velocity = v[0]
	return true


## Distance of the point above the water (negative = below).
func get_height_above(world_pos: Vector3) -> float:
	if sample(world_pos):
		return world_pos.y - height
	return 0.0
