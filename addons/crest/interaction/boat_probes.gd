@tool
class_name CrestBoatProbes
extends CrestFloatingObjectBase
## Multi-point buoyancy: each direct child Node3D is a buoyancy probe;
## forces are applied at the probe positions to the parent RigidBody3D.
## Port of Crest's BoatProbes.

## Multiplies the buoyancy force.
@export var force_multiplier := 1.0
## Vertical offset applied to probe positions when sampling.
@export var force_height_offset := 0.0
## Drag applied at each probe against its velocity relative to the water.
@export var drag := 1.0

var _body: RigidBody3D


func _ready() -> void:
	if Engine.is_editor_hint():
		return
	_body = get_body()
	if _body == null:
		push_warning("CrestBoatProbes: no RigidBody3D found on self or parents; disabled.")


func get_velocity() -> Vector3:
	return _body.linear_velocity if _body else Vector3.ZERO


func _physics_process(delta: float) -> void:
	if Engine.is_editor_hint() or _body == null:
		return
	var ocean := CrestOceanRenderer.instance
	if ocean == null:
		return

	_in_water = false
	var gravity := ProjectSettings.get_setting("physics/3d/default_gravity") as float
	for child in get_children():
		if not (child is Node3D):
			continue
		var probe := child as Node3D
		var pos := probe.global_position + Vector3.UP * force_height_offset
		var xz := Vector2(pos.x, pos.z)
		var out := [0.0]
		if not CrestCollision.sample_height(xz, out):
			continue
		var submersion := out[0] - pos.y
		if submersion <= 0.0:
			continue
		_in_water = true

		# Buoyancy proportional to submersion (per-probe share of the mass).
		var probe_mass := _body.mass / maxf(get_child_count(), 1)
		var force := Vector3.UP * gravity * submersion * force_multiplier * probe_mass

		# Drag against motion relative to the water surface.
		var point_vel := _body.linear_velocity + _body.angular_velocity.cross(pos - _body.global_position)
		force += -point_vel * drag * probe_mass

		_body.apply_force(force, pos - _body.global_position)
