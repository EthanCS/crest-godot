class_name CrestFFTCompute
extends RefCounted
## GPU FFT pipeline: spectrum initialisation, per-frame spectrum update and
## the separable IFFT producing tiled displacement textures. Port of
## Crest's FFTCompute (using global-memory butterfly passes instead of
## groupshared rows; mathematically equivalent).

const CASCADE_COUNT := 16

var resolution := 128

var spectrum_init: RID # RGBA32F, 16 layers
var spec_h: RID # RG32F
var spec_x: RID
var spec_z: RID
var temp_h: RID
var temp_x: RID
var temp_z: RID
var wave_buffers: RID # RGBA16F, 16 layers - final tiled displacements
var controls_buffer: RID

var repeat_sampler: RID

var _init_shader: CrestRDCompute
var _update_shader: CrestRDCompute
var _ifft_shader: CrestRDCompute
var _sample_shader: CrestRDCompute

var _rd: RenderingDevice
var _log2_res := 7
var _set_cache := {}


func initialize(p_resolution: int) -> bool:
	_rd = RenderingServer.get_rendering_device()
	if _rd == null:
		return false
	resolution = p_resolution
	_log2_res = int(log(resolution) / log(2.0))

	spectrum_init = _make_texture(RenderingDevice.DATA_FORMAT_R32G32B32A32_SFLOAT)
	spec_h = _make_texture(RenderingDevice.DATA_FORMAT_R32G32_SFLOAT)
	spec_x = _make_texture(RenderingDevice.DATA_FORMAT_R32G32_SFLOAT)
	spec_z = _make_texture(RenderingDevice.DATA_FORMAT_R32G32_SFLOAT)
	temp_h = _make_texture(RenderingDevice.DATA_FORMAT_R32G32_SFLOAT)
	temp_x = _make_texture(RenderingDevice.DATA_FORMAT_R32G32_SFLOAT)
	temp_z = _make_texture(RenderingDevice.DATA_FORMAT_R32G32_SFLOAT)
	wave_buffers = _make_texture(RenderingDevice.DATA_FORMAT_R16G16B16A16_SFLOAT)
	controls_buffer = _rd.storage_buffer_create(CrestWaveSpectrum.NUM_OCTAVES * 4)

	var ss := RDSamplerState.new()
	ss.repeat_u = RenderingDevice.SAMPLER_REPEAT_MODE_REPEAT
	ss.repeat_v = RenderingDevice.SAMPLER_REPEAT_MODE_REPEAT
	repeat_sampler = _rd.sampler_create(ss)

	_init_shader = CrestRDCompute.from_file(_rd, "res://addons/crest/shaders/sim/fft_spectrum_init.glsl")
	_update_shader = CrestRDCompute.from_file(_rd, "res://addons/crest/shaders/sim/fft_spectrum_update.glsl")
	_ifft_shader = CrestRDCompute.from_file(_rd, "res://addons/crest/shaders/sim/fft_ifft_pass.glsl")
	_sample_shader = CrestRDCompute.from_file(_rd, "res://addons/crest/shaders/sim/fft_to_wave_buffer.glsl")
	return _init_shader != null and _update_shader != null and _ifft_shader != null and _sample_shader != null


func _make_texture(p_format: RenderingDevice.DataFormat) -> RID:
	var fmt := RDTextureFormat.new()
	fmt.format = p_format
	fmt.width = resolution
	fmt.height = resolution
	fmt.depth = 1
	fmt.array_layers = CASCADE_COUNT
	fmt.texture_type = RenderingDevice.TEXTURE_TYPE_2D_ARRAY
	fmt.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT | RenderingDevice.TEXTURE_USAGE_STORAGE_BIT | RenderingDevice.TEXTURE_USAGE_CAN_UPDATE_BIT
	return _rd.texture_create(fmt, RDTextureView.new(), [])


## Rebuilds the initial spectrum (call when spectrum/wind/turbulence change).
func rebuild_spectrum(spectrum: CrestWaveSpectrum, wind_dir: Vector2, turbulence: float, seed: float) -> void:
	var controls := PackedFloat32Array()
	controls.resize(CrestWaveSpectrum.NUM_OCTAVES)
	for i in CrestWaveSpectrum.NUM_OCTAVES:
		controls[i] = 0.0 if spectrum.power_disabled[i] else pow(10.0, spectrum.power_log[i]) * spectrum.multiplier * spectrum.multiplier
	_rd.buffer_update(controls_buffer, 0, controls.size() * 4, controls.to_byte_array())

	var u_init := RDUniform.new()
	u_init.uniform_type = RenderingDevice.UNIFORM_TYPE_IMAGE
	u_init.binding = 0
	u_init.add_id(spectrum_init)
	var u_controls := RDUniform.new()
	u_controls.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	u_controls.binding = 1
	u_controls.add_id(controls_buffer)
	var set := _init_shader.make_uniform_set([u_init, u_controls])
	var pc := PackedFloat32Array([
		float(resolution), float(CASCADE_COUNT),
		CrestConstants.GRAVITY * spectrum.gravity_scale,
		spectrum.wind_speed, wind_dir.x, wind_dir.y, turbulence, seed,
	])
	var groups := resolution / CrestConstants.THREAD_GROUP_SIZE
	_init_shader.dispatch(groups, groups, CASCADE_COUNT, {0: set}, CrestRDCompute.pack_push_constants(pc))
	CrestRDCompute.free_rid_deferred(_rd, set)


## Advances the spectrum to the given time and runs the IFFT.
func advance_time(time: float, gravity_scale: float, chop: float) -> void:
	# Spectrum update.
	var u_init := RDUniform.new()
	u_init.uniform_type = RenderingDevice.UNIFORM_TYPE_SAMPLER_WITH_TEXTURE
	u_init.binding = 0
	u_init.add_id(repeat_sampler)
	u_init.add_id(spectrum_init)
	var set := _update_shader.make_uniform_set([u_init,
		_image_uniform(1, spec_h), _image_uniform(2, spec_x), _image_uniform(3, spec_z)])
	var pc := PackedFloat32Array([
		float(resolution), float(CASCADE_COUNT),
		CrestConstants.GRAVITY * gravity_scale, time, chop,
	])
	var groups := resolution / CrestConstants.THREAD_GROUP_SIZE
	_update_shader.dispatch(groups, groups, CASCADE_COUNT, {0: set}, CrestRDCompute.pack_push_constants(pc))
	CrestRDCompute.free_rid_deferred(_rd, set)

	# IFFT: log2(R) row passes then log2(R) column passes; the very last pass
	# writes the normalised real parts into wave_buffers.
	var cl := _rd.compute_list_begin()
	_rd.compute_list_bind_compute_pipeline(cl, _ifft_shader.pipeline)
	var total_passes := _log2_res * 2
	for pass_idx in total_passes:
		var is_row := pass_idx < _log2_res
		var local_pass := pass_idx if is_row else pass_idx - _log2_res
		var is_final := pass_idx == total_passes - 1
		# Ping-pong between spec_* and temp_* continuously across both
		# direction stages (row results feed the column stage).
		var even := (pass_idx % 2) == 0
		var in_h: RID = (spec_h if even else temp_h)
		var in_x: RID = (spec_x if even else temp_x)
		var in_z: RID = (spec_z if even else temp_z)
		var out_h: RID = (temp_h if even else spec_h)
		var out_x: RID = (temp_x if even else spec_x)
		var out_z: RID = (temp_z if even else spec_z)
		var set_key := ("a" if even else "b") + ("f" if is_final else "")
		var us: RID = _set_cache.get(set_key, RID())
		if not us.is_valid():
			us = _ifft_shader.make_uniform_set([
				_image_uniform(0, in_h), _image_uniform(1, in_x), _image_uniform(2, in_z),
				_image_uniform(3, out_h), _image_uniform(4, out_x), _image_uniform(5, out_z),
				_image_uniform(6, wave_buffers)])
			_set_cache[set_key] = us
		_rd.compute_list_bind_uniform_set(cl, us, 0)
		var pc_ifft := PackedFloat32Array([
			float(resolution), float(_log2_res), float(local_pass),
			0.0 if is_row else 1.0, 1.0 if is_final else 0.0,
		])
		var pc_bytes := CrestRDCompute.pack_push_constants(pc_ifft)
		_rd.compute_list_set_push_constant(cl, pc_bytes, pc_bytes.size())
		_rd.compute_list_dispatch(cl, resolution / 2 / CrestConstants.THREAD_GROUP_SIZE, groups, CASCADE_COUNT)
		_rd.compute_list_add_barrier(cl)
	_rd.compute_list_end()


## Samples the FFT output into the view-following wave buffer.
## slice_map: per-LOD FFT cascade indices; variance per LOD.
func sample_into_wave_buffer(wave_buffer: RID, cascade_buffer: RID, slice_map_buffer: RID, lod_transform: CrestLodTransform, accumulate: bool, weight: float, variance: float) -> void:
	var u_fft := RDUniform.new()
	u_fft.uniform_type = RenderingDevice.UNIFORM_TYPE_SAMPLER_WITH_TEXTURE
	u_fft.binding = 0
	u_fft.add_id(repeat_sampler)
	u_fft.add_id(wave_buffers)
	var u_out := RDUniform.new()
	u_out.uniform_type = RenderingDevice.UNIFORM_TYPE_IMAGE
	u_out.binding = 1
	u_out.add_id(wave_buffer)
	var u_map := RDUniform.new()
	u_map.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	u_map.binding = 2
	u_map.add_id(slice_map_buffer)
	var u_cascades := RDUniform.new()
	u_cascades.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	u_cascades.binding = 3
	u_cascades.add_id(cascade_buffer)
	var set := _sample_shader.make_uniform_set([u_fft, u_out, u_map, u_cascades])
	var pc := PackedFloat32Array([
		float(lod_transform.lod_data_resolution), float(lod_transform.lod_count),
		1.0 if accumulate else 0.0, weight, variance,
	])
	var groups := lod_transform.lod_data_resolution / CrestConstants.THREAD_GROUP_SIZE
	_sample_shader.dispatch(groups, groups, lod_transform.lod_count, {0: set}, CrestRDCompute.pack_push_constants(pc))
	CrestRDCompute.free_rid_deferred(_rd, set)


func _image_uniform(binding: int, tex: RID) -> RDUniform:
	var u := RDUniform.new()
	u.uniform_type = RenderingDevice.UNIFORM_TYPE_IMAGE
	u.binding = binding
	u.add_id(tex)
	return u


func free_rids() -> void:
	if _rd:
		for rid in [spectrum_init, spec_h, spec_x, spec_z, temp_h, temp_x, temp_z, wave_buffers, controls_buffer, repeat_sampler]:
			if rid is RID and rid.is_valid():
				_rd.free_rid(rid)
		for key in _set_cache:
			_rd.free_rid(_set_cache[key])
		for shader in [_init_shader, _update_shader, _ifft_shader, _sample_shader]:
			if shader:
				shader.free_rid()
