@tool
class_name CrestSimpleFloatingObject
extends CrestFloatingObjectBase
## Applies a simple approximation of buoyancy: force based on submerged
## depth and torque based on alignment to the water normal.
## Port of Crest's SimpleFloatingObject (attach to a RigidBody3D or as a
## child of one).

@export_group("Buoyancy Force")
## Offsets the centre of the object to raise it (or lower it) in the water.
@export var raise_object := 1.0
## Strength of buoyancy force per meter of submersion cubed.
@export var buoyancy_coeff := 3.0
## Strength of torque applied to match the object orientation to the water
## normal.
@export var buoyancy_torque := 8.0
## Approximate hydrodynamics of 'surfing' down waves.
@export_range(0.0, 1.0) var accelerate_downhill := 0.0
## Clamps the buoyancy force (useful for fully submerged objects). <= 0
## disables the clamp.
@export var maximum_buoyancy_force := 0.0

@export_group("Drag")
## Vertical offset for where the drag force is applied.
@export var force_height_offset := -0.3
@export var drag_in_water_up := 3.0
@export var drag_in_water_right := 2.0
@export var drag_in_water_forward := 1.0
@export var drag_in_water_rotational := 0.2

var _body: RigidBody3D


func _ready() -> void:
	if Engine.is_editor_hint():
		return
	_body = get_body()
	if _body == null:
		push_warning("CrestSimpleFloatingObject: no RigidBody3D found on self or parents; disabled.")


func get_velocity() -> Vector3:
	return _body.linear_velocity if _body else Vector3.ZERO


func _physics_process(delta: float) -> void:
	if Engine.is_editor_hint() or _body == null:
		return
	var ocean := CrestOceanRenderer.instance
	if ocean == null:
		return

	var pos := _body.global_position
	var xz := Vector2(pos.x, pos.z)
	var disp := CrestCollision.sample_displacement(xz)
	var normal := CrestCollision.sample_normal(xz, maxf(object_width * 0.5, 0.1))
	var water_vel := Vector3.ZERO
	var h := [0.0]
	var v := [0.0]
	if CrestCollision.sample_height_and_velocity(xz, delta, h, v):
		water_vel = Vector3(0.0, v[0], 0.0)

	var velocity_relative_to_water := get_velocity() - water_vel

	var water_height := disp.y + ocean.ocean_level
	# Crest: bottomDepth = height - pos.y + raiseObject.
	var bottom_depth := water_height - pos.y + raise_object

	_in_water = bottom_depth > 0.0
	if not _in_water:
		return

	var gravity := ProjectSettings.get_setting("physics/3d/default_gravity") as float
	var gravity_dir := Vector3.DOWN

	var buoyancy: Vector3 = buoyancy_coeff * bottom_depth * bottom_depth * bottom_depth * -gravity_dir
	if maximum_buoyancy_force > 0.0:
		buoyancy = buoyancy.limit_length(maximum_buoyancy_force)
	_body.apply_central_force(buoyancy * _body.mass)

	# Approximate hydrodynamics of sliding along water.
	if accelerate_downhill > 0.0:
		_body.apply_central_force(accelerate_downhill * gravity * Vector3(normal.x, 0.0, normal.z) * _body.mass)

	# Drag relative to water.
	var force_position := _body.global_position + force_height_offset * Vector3.UP
	var up_drag := drag_in_water_up * -velocity_relative_to_water.dot(Vector3.UP) * Vector3.UP
	_body.apply_force(up_drag * _body.mass, force_position - _body.global_position)
	var right := _body.global_transform.basis.x
	var right_drag := drag_in_water_right * -velocity_relative_to_water.dot(right) * right
	_body.apply_force(right_drag * _body.mass, force_position - _body.global_position)
	var forward := -_body.global_transform.basis.z
	var fwd_drag := drag_in_water_forward * -velocity_relative_to_water.dot(forward) * forward
	_body.apply_force(fwd_drag * _body.mass, force_position - _body.global_position)

	# Align to water normal.
	var torque_width := _body.global_transform.basis.y.cross(normal)
	_body.apply_torque(torque_width * buoyancy_torque * _body.mass)
	_body.apply_torque(-drag_in_water_rotational * _body.angular_velocity * _body.mass)
