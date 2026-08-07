class_name CrestCollision
extends RefCounted
## Collision/query API for the ocean surface. Port of Crest's collision
## providers + sampling helpers.
##
## Two data sources:
## - ANALYTIC: exact CPU mirror of the Gerstner evaluation (works whenever
##   the shape generators are CrestShapeGerstner).
## - READBACK: async GPU readback of the animated-waves cascade (works with
##   any generator, one or more frames of latency). Not yet implemented.

## Returns the water surface height (world Y) at the given world XZ, or
## false if no ocean/data available.
static func sample_height(world_xz: Vector2, out_height: Array) -> bool:
	var ocean := CrestOceanRenderer.instance
	if ocean == null:
		return false
	var disp := sample_displacement(world_xz)
	out_height[0] = ocean.ocean_level + disp.y
	return true


## Displacement (xyz) of the water surface at world XZ, analytically
## summed over all Gerstner generators.
static func sample_displacement(world_xz: Vector2) -> Vector3:
	var ocean := CrestOceanRenderer.instance
	if ocean == null:
		return Vector3.ZERO
	var disp := Vector3.ZERO
	var time := ocean.current_time()
	for node in ocean.get_tree().get_nodes_in_group(&"crest_shape_generator"):
		if node is CrestShapeGerstner:
			disp += node.compute_displacement(world_xz, time)
	return disp


## Surface normal from finite differences of the analytic displacement.
static func sample_normal(world_xz: Vector2, epsilon := 0.1) -> Vector3:
	var d := sample_displacement(world_xz)
	var dx := sample_displacement(world_xz + Vector2(epsilon, 0.0))
	var dz := sample_displacement(world_xz + Vector2(0.0, epsilon))
	var tx := Vector3(epsilon, 0.0, 0.0) + (dx - d)
	var tz := Vector3(0.0, 0.0, epsilon) + (dz - d)
	return tz.cross(tx).normalized()


## Undisturbed height + surface vertical velocity helper result.
static func sample_height_and_velocity(world_xz: Vector2, dt: float, out_height: Array, out_velocity: Array) -> bool:
	var ocean := CrestOceanRenderer.instance
	if ocean == null:
		return false
	var time := ocean.current_time()
	var disp := Vector3.ZERO
	var disp_dt := Vector3.ZERO
	for node in ocean.get_tree().get_nodes_in_group(&"crest_shape_generator"):
		if node is CrestShapeGerstner:
			disp += node.compute_displacement(world_xz, time)
			disp_dt += node.compute_displacement(world_xz, time - dt)
	out_height[0] = ocean.ocean_level + disp.y
	out_velocity[0] = (disp.y - disp_dt.y) / maxf(dt, 1e-5)
	return true
