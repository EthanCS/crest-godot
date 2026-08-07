extends Node3D
## Demo boat: circles the pond, floats on the waves and stirs up ripples
## via CrestSphereWaterInteraction (dynamic waves sim).

@export var radius := 12.0
@export var speed := 4.0
@export var draft := 0.45
## Smoothing of the height follow (1/s); avoids the hull punching through
## waves on transients.
@export var follow_smoothing := 6.0

var _angle := 0.0
var _center := Vector2.ZERO
var _smooth_y := 0.0


@export var center := Vector2.ZERO


func _ready() -> void:
	_center = center
	_smooth_y = global_position.y


func _physics_process(delta: float) -> void:
	var ocean := CrestOceanRenderer.instance
	if ocean == null:
		return
	_angle += speed / radius * delta
	var pos := _center + Vector2(cos(_angle), sin(_angle)) * radius

	# Follow the water surface, smoothed so the hull doesn't punch through.
	var water_y := ocean.ocean_level
	var out := [0.0]
	if CrestCollision.sample_height(pos, out):
		water_y = out[0]
	_smooth_y = lerpf(_smooth_y, water_y + draft, 1.0 - exp(-follow_smoothing * delta))
	global_position = Vector3(pos.x, _smooth_y, pos.y)

	# Heading along the tangent; hull (long axis = local X) points forward.
	var heading := Vector3(-sin(_angle), 0.0, cos(_angle))
	var normal := CrestCollision.sample_normal(pos, 1.5)
	var right := normal.cross(heading).normalized()
	var forward := right.cross(normal).normalized()
	global_basis = Basis(forward, normal, -right).orthonormalized()
