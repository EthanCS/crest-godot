class_name CrestLodDataMgrDynWaves
extends CrestLodDataMgr
## Dynamic waves sim: damped wave equation with fixed-frequency substepping
## and sphere interaction injection. Port of Crest's LodDataMgrDynWaves.

const MAX_SPHERES := 256

var settings := CrestSimSettingsWave.new()

var _update: CrestRDCompute
var _inject: CrestRDCompute
var _time_to_simulate := 0.0

var _sphere_ssbo: RID
var _sphere_data := PackedFloat32Array()


func init_mgr(p_resolution: int, p_layers: int, p_settings: CrestSimSettingsWave) -> void:
	settings = p_settings if p_settings else CrestSimSettingsWave.new()
	init_sim(p_resolution, p_layers, RenderingDevice.DATA_FORMAT_R16G16_SFLOAT, true)
	_update = CrestRDCompute.from_file(rd, "res://addons/crest/shaders/sim/update_dyn_waves.glsl")
	_inject = CrestRDCompute.from_file(rd, "res://addons/crest/shaders/sim/inject_dyn_waves.glsl")
	_sphere_ssbo = rd.storage_buffer_create(MAX_SPHERES * 8 * 4)


## Runs one frame of the sim.
## spheres: array of Dictionaries with keys pos (Vector2), vel (Vector3),
## radius, weight.
func update_sim(delta: float, lod_transform: CrestLodTransform, cascade_current: RID, cascade_source: RID, flow_mgr: CrestLodDataMgr, depth_mgr: CrestLodDataMgr, ocean_level: float, gravity: float, lod_change: float, spheres: Array) -> void:
	if _update == null:
		return
	_time_to_simulate += delta
	var freq := settings.simulation_frequency
	var num_substeps := floori(_time_to_simulate * freq)
	var substep_dt := 1.0 / freq
	if num_substeps == 0:
		# Still advect once to compensate for camera motion.
		num_substeps = 1
		substep_dt = 0.0
	# Crest discards leftover time beyond what substeps consume.
	_time_to_simulate -= num_substeps * substep_dt

	for i in num_substeps:
		_dispatch_update(substep_dt, lod_transform, cascade_current, cascade_source, flow_mgr, depth_mgr, ocean_level, gravity, lod_change, i == 0)
		swap_targets()
		if substep_dt > 0.0 and not spheres.is_empty():
			_dispatch_inject(substep_dt, lod_transform, cascade_current, spheres)


func _dispatch_update(dt: float, lod_transform: CrestLodTransform, cascade_current: RID, cascade_source: RID, flow_mgr: CrestLodDataMgr, depth_mgr: CrestLodDataMgr, ocean_level: float, gravity: float, lod_change: float, use_source_transforms: bool) -> void:
	var u_cur := RDUniform.new()
	u_cur.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	u_cur.binding = 0
	u_cur.add_id(cascade_current)
	var u_src := RDUniform.new()
	u_src.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	u_src.binding = 1
	u_src.add_id(cascade_source)

	var u_source := make_sampled_uniform(2)
	var u_target := make_image_uniform(3)
	var u_flow := flow_mgr.make_sampled_uniform(4) if flow_mgr else _fallback_uniform(4)
	var u_depth := depth_mgr.make_sampled_uniform(5) if depth_mgr else _fallback_uniform(5)

	var set := _update.make_uniform_set([u_cur, u_src, u_source, u_target, u_flow, u_depth])
	var pc := PackedFloat32Array([
		float(resolution), float(layer_count), dt,
		gravity * settings.gravity_multiplier,
		settings.damping, settings.courant_number, settings.attenuation_in_shallows,
		lod_change, ocean_level, 1.0 if use_source_transforms else 0.0,
	])
	_update.dispatch(resolution / CrestConstants.THREAD_GROUP_SIZE, resolution / CrestConstants.THREAD_GROUP_SIZE, layer_count, {0: set}, CrestRDCompute.pack_push_constants(pc))
	CrestRDCompute.free_rid_deferred(rd, set)


func _dispatch_inject(dt: float, lod_transform: CrestLodTransform, cascade_current: RID, spheres: Array) -> void:
	_sphere_data.resize(0)
	var count := mini(spheres.size(), MAX_SPHERES)
	for i in count:
		var s: Dictionary = spheres[i]
		var pos: Vector2 = s["pos"]
		var vel: Vector3 = s["vel"]
		_sphere_data.append_array([pos.x, pos.y, s["radius"], s["weight"], vel.x, vel.y, vel.z, 0.0])
	if count > 0:
		rd.buffer_update(_sphere_ssbo, 0, _sphere_data.size() * 4, _sphere_data.to_byte_array())

	var u_cascades := RDUniform.new()
	u_cascades.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	u_cascades.binding = 0
	u_cascades.add_id(cascade_current)
	var u_spheres := RDUniform.new()
	u_spheres.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	u_spheres.binding = 1
	u_spheres.add_id(_sphere_ssbo)
	var u_dyn := make_image_uniform(2, false)

	var set := _inject.make_uniform_set([u_cascades, u_spheres, u_dyn])
	var pc := PackedFloat32Array([
		float(resolution), float(layer_count), float(count), dt,
		0.5, 1.55, 0.109, # weight_up_down_mul, inner_sphere_multiplier, inner_sphere_offset
	])
	_inject.dispatch(resolution / CrestConstants.THREAD_GROUP_SIZE, resolution / CrestConstants.THREAD_GROUP_SIZE, layer_count, {0: set}, CrestRDCompute.pack_push_constants(pc))
	CrestRDCompute.free_rid_deferred(rd, set)


var _fallback: CrestLodDataMgr


func _fallback_uniform(binding: int) -> RDUniform:
	if _fallback == null:
		_fallback = CrestLodDataMgr.new()
		_fallback.init_sim(1, 2, RenderingDevice.DATA_FORMAT_R16G16_SFLOAT, false)
	var u := _fallback.make_sampled_uniform(binding)
	return u


func free_rids() -> void:
	if rd:
		if _update:
			_update.free_rid()
		if _inject:
			_inject.free_rid()
		if _sphere_ssbo.is_valid():
			rd.free_rid(_sphere_ssbo)
		if _fallback:
			_fallback.free_rids()
	super.free_rids()
