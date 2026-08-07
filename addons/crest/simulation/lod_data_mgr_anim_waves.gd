class_name CrestLodDataMgrAnimWaves
extends CrestLodDataMgr
## Animated waves manager: owns the intermediate per-LOD wave buffer that
## shape generators (Gerstner / FFT) evaluate into, and the final animated
## waves cascade (RGB = displacement, A = variance) produced by the combine
## pass. Port of Crest's LodDataMgrAnimWaves.

var wave_buffer: RID

var _combine: CrestRDCompute
var _clear: CrestRDCompute
var _fallback_dyn_waves: RID


func init_mgr(p_resolution: int, p_layers: int) -> void:
	init_sim(p_resolution, p_layers, RenderingDevice.DATA_FORMAT_R16G16B16A16_SFLOAT, false)

	var fmt := RDTextureFormat.new()
	fmt.format = data_format
	fmt.width = resolution
	fmt.height = resolution
	fmt.depth = 1
	fmt.array_layers = p_layers
	fmt.texture_type = RenderingDevice.TEXTURE_TYPE_2D_ARRAY
	fmt.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT | RenderingDevice.TEXTURE_USAGE_STORAGE_BIT
	wave_buffer = rd.texture_create(fmt, RDTextureView.new(), [])

	_fallback_dyn_waves = _make_fallback(RenderingDevice.DATA_FORMAT_R16G16_SFLOAT)

	_combine = CrestRDCompute.from_file(rd, "res://addons/crest/shaders/sim/shape_combine.glsl")
	_clear = CrestRDCompute.from_file(rd, "res://addons/crest/shaders/sim/clear.glsl")


## Runs the shape generators and the combine pass.
## shapes: array of generators with evaluate(wave_buffer, depth_mgr, lod_transform, scale, level, time, accumulate).
## cascade_buffer: storage buffer with the packed cascade params (current frame).
func update(shapes: Array, depth_mgr: CrestLodDataMgr, dyn_waves_mgr: CrestLodDataMgr, lod_transform: CrestLodTransform, cascade_buffer: RID, ocean_scale: float, ocean_level: float, time: float, dyn_waves_settings: Resource) -> void:
	if wave_buffer.is_valid() == false or _combine == null:
		return

	if shapes.is_empty():
		var us_clear := make_image_uniform(0, false)
		var set_clear := _clear.make_uniform_set([us_clear])
		_clear.dispatch(resolution / CrestConstants.THREAD_GROUP_SIZE, resolution / CrestConstants.THREAD_GROUP_SIZE, layer_count,
			{0: set_clear}, CrestRDCompute.pack_push_constants(PackedFloat32Array([float(resolution), 0.0, 0.0, 0.0, 0.0])))
		CrestRDCompute.free_rid_deferred(rd, set_clear)
	else:
		var first := true
		for shape in shapes:
			shape.evaluate(wave_buffer, depth_mgr, lod_transform, ocean_scale, ocean_level, time, not first)
			first = false

	# Combine pass.
	var dyn_enabled := dyn_waves_mgr != null
	var u_cascades := RDUniform.new()
	u_cascades.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	u_cascades.binding = 0
	u_cascades.add_id(cascade_buffer)

	var u_wave := RDUniform.new()
	u_wave.uniform_type = RenderingDevice.UNIFORM_TYPE_SAMPLER_WITH_TEXTURE
	u_wave.binding = 1
	u_wave.add_id(sampler)
	u_wave.add_id(wave_buffer)

	var u_out := make_image_uniform(2, false)

	var u_dyn := RDUniform.new()
	u_dyn.uniform_type = RenderingDevice.UNIFORM_TYPE_SAMPLER_WITH_TEXTURE
	u_dyn.binding = 3
	u_dyn.add_id(sampler)
	u_dyn.add_id(dyn_waves_mgr.current_texture() if dyn_enabled else _fallback_dyn_waves)

	var set_combine := _combine.make_uniform_set([u_cascades, u_wave, u_out, u_dyn])
	var pc := PackedFloat32Array([
		float(resolution), float(layer_count),
		dyn_waves_settings.horiz_displace if dyn_waves_settings else 3.0,
		dyn_waves_settings.displace_clamp if dyn_waves_settings else 0.3,
		1.0 if dyn_enabled else 0.0,
	])
	_combine.dispatch(resolution / CrestConstants.THREAD_GROUP_SIZE, resolution / CrestConstants.THREAD_GROUP_SIZE, layer_count,
		{0: set_combine}, CrestRDCompute.pack_push_constants(pc))
	CrestRDCompute.free_rid_deferred(rd, set_combine)


func _make_fallback(p_format: RenderingDevice.DataFormat) -> RID:
	var fmt := RDTextureFormat.new()
	fmt.format = p_format
	fmt.width = 1
	fmt.height = 1
	fmt.array_layers = 1
	fmt.texture_type = RenderingDevice.TEXTURE_TYPE_2D_ARRAY
	fmt.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT | RenderingDevice.TEXTURE_USAGE_CAN_UPDATE_BIT
	return rd.texture_create(fmt, RDTextureView.new(), [PackedByteArray([0, 0, 0, 0])])


func free_rids() -> void:
	if rd:
		if wave_buffer.is_valid():
			rd.free_rid(wave_buffer)
		if _fallback_dyn_waves.is_valid():
			rd.free_rid(_fallback_dyn_waves)
		if _combine:
			_combine.free_rid()
		if _clear:
			_clear.free_rid()
	super.free_rids()
