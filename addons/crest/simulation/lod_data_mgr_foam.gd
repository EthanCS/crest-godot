class_name CrestLodDataMgrFoam
extends CrestLodDataMgr
## Foam sim: advection + dissipation + crest/shoreline injection, with
## fixed-frequency substepping. Port of Crest's LodDataMgrFoam.

var settings := CrestSimSettingsFoam.new()

var _update: CrestRDCompute
var _inject: CrestRDCompute
var _time_to_simulate := 0.0
var _needs_prewarm := true


func init_mgr(p_resolution: int, p_layers: int, p_settings: CrestSimSettingsFoam) -> void:
	settings = p_settings if p_settings else CrestSimSettingsFoam.new()
	init_sim(p_resolution, p_layers, RenderingDevice.DATA_FORMAT_R16_SFLOAT, true)
	_update = CrestRDCompute.from_file(rd, "res://addons/crest/shaders/sim/update_foam.glsl")
	_inject = CrestRDCompute.from_file(rd, "res://addons/crest/shaders/sim/inject_foam.glsl")


## Call when the viewer teleports far: the sim prewarms next frame.
func notify_teleport() -> void:
	_needs_prewarm = true


func update_sim(delta: float, lod_transform: CrestLodTransform, cascade_current: RID, cascade_source: RID, flow_mgr: CrestLodDataMgr, anim_waves_mgr: CrestLodDataMgr, depth_mgr: CrestLodDataMgr, ocean_level: float, lod_change: float) -> void:
	if _update == null:
		return
	_time_to_simulate += delta
	var freq := settings.simulation_frequency
	var num_substeps := floori(_time_to_simulate * freq)
	var substep_dt := 1.0 / freq
	if num_substeps == 0:
		num_substeps = 1
		substep_dt = 0.0
	_time_to_simulate -= num_substeps * substep_dt

	for i in num_substeps:
		_dispatch(substep_dt, lod_transform, cascade_current, cascade_source, flow_mgr, anim_waves_mgr, depth_mgr, ocean_level, lod_change, i == 0)
		swap_targets()
		_needs_prewarm = false


func _dispatch(dt: float, lod_transform: CrestLodTransform, cascade_current: RID, cascade_source: RID, flow_mgr: CrestLodDataMgr, anim_waves_mgr: CrestLodDataMgr, depth_mgr: CrestLodDataMgr, ocean_level: float, lod_change: float, use_source_transforms: bool) -> void:
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
	var u_waves := anim_waves_mgr.make_sampled_uniform(5)
	var u_depth := depth_mgr.make_sampled_uniform(6) if depth_mgr else _fallback_uniform(6)

	var set := _update.make_uniform_set([u_cur, u_src, u_source, u_target, u_flow, u_waves, u_depth])
	var prewarm := 1.0 if (_needs_prewarm and settings.prewarm) else 0.0
	var pc := PackedFloat32Array([
		float(resolution), float(layer_count), dt, lod_change, ocean_level,
		1.0 if use_source_transforms else 0.0,
		settings.foam_fade_rate, settings.wave_foam_strength, settings.wave_foam_coverage,
		float(settings.filter_waves), settings.shoreline_foam_max_depth, settings.shoreline_foam_strength,
		prewarm,
	])
	_update.dispatch(resolution / CrestConstants.THREAD_GROUP_SIZE, resolution / CrestConstants.THREAD_GROUP_SIZE, layer_count, {0: set}, CrestRDCompute.pack_push_constants(pc))
	CrestRDCompute.free_rid_deferred(rd, set)


var _fallback: CrestLodDataMgr


func _fallback_uniform(binding: int) -> RDUniform:
	if _fallback == null:
		_fallback = CrestLodDataMgr.new()
		_fallback.init_sim(1, 2, RenderingDevice.DATA_FORMAT_R16G16_SFLOAT, false)
	return _fallback.make_sampled_uniform(binding)


## Injects foam inputs (Crest: RegisterFoamInput).
## inputs: array of Dictionaries:
##   rect_center/rect_half_size: Vector2 world XZ
##   strength: float
##   mode: 0 = texture patch, 1 = sphere patch
##   texture: Texture2D or null
func inject_inputs(lod_transform: CrestLodTransform, cascade_current: RID, inputs: Array, time: float) -> void:
	if _inject == null:
		return
	for input in inputs:
		_dispatch_inject(lod_transform, cascade_current, input, time)


func _dispatch_inject(lod_transform: CrestLodTransform, cascade_current: RID, input: Dictionary, time: float) -> void:
	var u_cascades := RDUniform.new()
	u_cascades.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	u_cascades.binding = 0
	u_cascades.add_id(cascade_current)
	var u_tex := RDUniform.new()
	u_tex.uniform_type = RenderingDevice.UNIFORM_TYPE_SAMPLER_WITH_TEXTURE
	u_tex.binding = 1
	u_tex.add_id(sampler)
	u_tex.add_id(_get_texture_rd(input.get("texture")))
	var u_target := make_image_uniform(2, false)

	var rect_center: Vector2 = input["rect_center"]
	var rect_half: Vector2 = input["rect_half_size"]
	var set := _inject.make_uniform_set([u_cascades, u_tex, u_target])
	var pc := PackedFloat32Array([
		float(resolution), float(layer_count),
		rect_center.x, rect_center.y, rect_half.x, rect_half.y,
		input.get("strength", 1.0), input.get("mode", 0.0),
		1.0 if input.get("texture") != null else 0.0, time,
	])
	_inject.dispatch(resolution / CrestConstants.THREAD_GROUP_SIZE, resolution / CrestConstants.THREAD_GROUP_SIZE, layer_count, {0: set}, CrestRDCompute.pack_push_constants(pc))
	CrestRDCompute.free_rid_deferred(rd, set)


var _tex_cache := {}
var _fallback_tex: RID


func _get_texture_rd(texture: Texture2D) -> RID:
	if texture == null:
		return _get_fallback_tex()
	var key := texture.get_instance_id()
	if _tex_cache.has(key):
		return _tex_cache[key]
	var img := texture.get_image()
	if img == null:
		return _get_fallback_tex()
	img.convert(Image.FORMAT_RGBAF)
	var fmt := RDTextureFormat.new()
	fmt.format = RenderingDevice.DATA_FORMAT_R32G32B32A32_SFLOAT
	fmt.width = img.get_width()
	fmt.height = img.get_height()
	fmt.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT | RenderingDevice.TEXTURE_USAGE_CAN_UPDATE_BIT
	var rid := rd.texture_create(fmt, RDTextureView.new(), [img.get_data()])
	_tex_cache[key] = rid
	return rid


func _get_fallback_tex() -> RID:
	if not _fallback_tex.is_valid():
		var fmt := RDTextureFormat.new()
		fmt.format = RenderingDevice.DATA_FORMAT_R32G32B32A32_SFLOAT
		fmt.width = 1
		fmt.height = 1
		fmt.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT | RenderingDevice.TEXTURE_USAGE_CAN_UPDATE_BIT
		var data := PackedFloat32Array([1.0, 1.0, 1.0, 1.0]).to_byte_array()
		_fallback_tex = rd.texture_create(fmt, RDTextureView.new(), [data])
	return _fallback_tex


func free_rids() -> void:
	if rd:
		if _update:
			_update.free_rid()
		if _inject:
			_inject.free_rid()
		for key in _tex_cache:
			rd.free_rid(_tex_cache[key])
		if _fallback_tex.is_valid():
			rd.free_rid(_fallback_tex)
		if _fallback:
			_fallback.free_rids()
	super.free_rids()
