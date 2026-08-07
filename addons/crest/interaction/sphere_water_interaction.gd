@tool
class_name CrestSphereWaterInteraction
extends Node3D
## Generates dynamic waves and foam for a spherical object moving through
## the water. Port of Crest's SphereWaterInteraction: the CPU side computes
## the object velocity (with teleport/clamp handling) and submersion-based
## weight; the GPU side injects the SDF force into the dynamic waves sim.

@export var radius := 1.0
## Overall strength multiplier.
@export var weight := 1.0
## Up/down motion is weighted down relative to horizontal.
@export var weight_up_down_mul := 0.5
## Compensates for the vertical motion of the waves themselves (0 = off).
@export_range(0.0, 1.0) var compensate_for_wave_motion := 0.45
## Foam generation strength at the contact area.
@export var foam_strength := 0.5

## Speeds above this (m/s) are treated as teleports (no wave response).
## Crest default: 500 km/h.
@export var teleport_speed := 138.9
## Speeds are clamped to this (m/s). Crest default: 100 km/h.
@export var max_speed := 27.8

var _pos_last := Vector3.ZERO
var _has_last := false
var _vel := Vector3.ZERO


func _enter_tree() -> void:
	add_to_group(&"crest_sphere_interaction")


func _physics_process(delta: float) -> void:
	if Engine.is_editor_hint():
		return
	var pos := global_position
	if not _has_last:
		_pos_last = pos
		_has_last = true
		return
	var vel := (pos - _pos_last) / maxf(delta, 1e-5)
	_pos_last = pos
	if vel.length() > teleport_speed:
		vel = Vector3.ZERO
	elif vel.length() > max_speed:
		vel = vel.normalized() * max_speed
	_vel = vel


## Called by the ocean renderer each dynamics frame.
func get_sphere_injection() -> Dictionary:
	var ocean := CrestOceanRenderer.instance
	if ocean == null:
		return {}
	var pos := global_position
	var xz := Vector2(pos.x, pos.z)

	var relative_vel := _vel
	relative_vel.y *= weight_up_down_mul

	# Compensate for wave motion so bobbing objects don't generate waves.
	var h := [0.0]
	var v := [0.0]
	var water_height := ocean.ocean_level
	if CrestCollision.sample_height_and_velocity(xz, 1.0 / 60.0, h, v):
		water_height = h[0]
		relative_vel.y -= compensate_for_wave_motion * v[0]

	var w := 3.75 * weight
	# Gravity-normalised (Crest: sqrt(gravityMultiplier)/5 with default 1/25).
	w *= 1.0 / 5.0

	# Submersion-based weight modulation.
	var height_above := pos.y - water_height
	if height_above < 0.0:
		var depth_ratio := -height_above / maxf(radius, 0.01)
		w *= exp(-pow(depth_ratio * 0.5, 2.0))
	else:
		w *= sqrt(maxf(0.0, 1.0 - height_above / maxf(radius, 0.01)))

	if w < 0.001:
		return {}

	return {
		"pos": xz,
		"vel": relative_vel,
		"radius": radius * 1.1, # Crest: slightly larger helps wrap the object
		"weight": w,
		"foam": foam_strength * w,
	}
