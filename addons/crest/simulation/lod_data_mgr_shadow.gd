class_name CrestLodDataMgrShadow
extends CrestLodDataMgr
## Shadow sim: per-cascade soft/hard shadow factors for the water surface.
## Port of Crest's LodDataMgrShadow (jitter + EMA accumulation).
##
## Occlusion sources are analytic sphere casters registered via
## CrestRegisterShadowInput (see update_shadow.glsl header for why).

const MAX_CASTERS := 128

var settings := CrestSimSettingsShadow.new()

var _update: CrestRDCompute
var _caster_ssbo: RID
var _caster_data := PackedFloat32Array()
## Direction towards the main light, set by the ocean renderer each frame.
var light_dir := Vector3(0, 1, 0)


func init_mgr(p_resolution: int, p_layers: int, p_settings: CrestSimSettingsShadow) -> void:
	settings = p_settings if p_settings else CrestSimSettingsShadow.new()
	init_sim(p_resolution, p_layers, RenderingDevice.DATA_FORMAT_R8G8_UNORM, true, Color(1, 1, 0, 0))
	_update = CrestRDCompute.from_file(rd, "res://addons/crest/shaders/sim/update_shadow.glsl")
	_caster_ssbo = rd.storage_buffer_create(MAX_CASTERS * 4 * 4)


## casters: array of Dictionaries { pos: Vector3, radius: float }.
func update_sim(delta: float, lod_transform: CrestLodTransform, cascade_current: RID, cascade_source: RID, anim_waves_mgr: CrestLodDataMgr, ocean_level: float, lod_change: float, time: float, casters: Array) -> void:
	if _update == null:
		return

	_caster_data.resize(0)
	var count := mini(casters.size(), MAX_CASTERS)
	for i in count:
		var c: Dictionary = casters[i]
		var pos: Vector3 = c["pos"]
		_caster_data.append_array([pos.x, pos.y, pos.z, c["radius"]])
	if count > 0:
		rd.buffer_update(_caster_ssbo, 0, _caster_data.size() * 4, _caster_data.to_byte_array())

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
	var u_casters := RDUniform.new()
	u_casters.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	u_casters.binding = 4
	u_casters.add_id(_caster_ssbo)
	var u_waves := anim_waves_mgr.make_sampled_uniform(5)

	var set := _update.make_uniform_set([u_cur, u_src, u_source, u_target, u_casters, u_waves])
	var pc := PackedFloat32Array([
		float(resolution), float(layer_count), delta, lod_change, float(count),
		settings.jitter_diameter_soft, settings.current_frame_weight_soft,
		settings.jitter_diameter_hard, settings.current_frame_weight_hard,
		light_dir.x, light_dir.y, light_dir.z,
		time, ocean_level, 1.0,
	])
	_update.dispatch(resolution / CrestConstants.THREAD_GROUP_SIZE, resolution / CrestConstants.THREAD_GROUP_SIZE, layer_count, {0: set}, CrestRDCompute.pack_push_constants(pc))
	CrestRDCompute.free_rid_deferred(rd, set)
	swap_targets()


func free_rids() -> void:
	if rd:
		if _update:
			_update.free_rid()
		if _caster_ssbo.is_valid():
			rd.free_rid(_caster_ssbo)
	super.free_rids()
