@tool
class_name CrestShapeGerstner
extends Node3D
## Gerstner wave generator. Port of Crest's ShapeGerstner: samples wave
## components from a CrestWaveSpectrum, buckets them per LOD cascade by
## wavelength, and evaluates them on the GPU into the animated waves
## cascade buffer.

## Emitted when wave data changed and buffers need rebuilding.
signal wave_data_changed

## The spectrum the waves are sampled from.
@export var spectrum: CrestWaveSpectrum:
	set(value):
		if spectrum and spectrum.changed.is_connected(_on_spectrum_changed):
			spectrum.changed.disconnect(_on_spectrum_changed)
		spectrum = value
		if spectrum and not spectrum.changed.is_connected(_on_spectrum_changed):
			spectrum.changed.connect(_on_spectrum_changed)
		_dirty = true

## Number of wave components sampled per spectrum octave.
@export_range(1, 16) var components_per_octave := 8:
	set(value):
		components_per_octave = value
		_dirty = true

## Seed for stratified sampling. 0 = randomise on every build.
@export var random_seed := 0:
	set(value):
		random_seed = value
		_dirty = true

## Overall weight of this wave generator.
@export_range(0.0, 1.0) var weight := 1.0:
	set(value):
		weight = value
		_dirty = true

## Weight of the counter-propagating component of each wave pair
## (Crest: ShapeGerstner reverse wave weight).
@export_range(0.0, 1.0) var reverse_wave_weight := 0.5:
	set(value):
		reverse_wave_weight = value
		_dirty = true

## How much waves attenuate in shallow water (0 = off).
@export_range(0.0, 1.0) var attenuation_in_shallows := 0.95:
	set(value):
		attenuation_in_shallows = value
		_dirty = true

## Maximum wave components (Crest: ShapeGerstner MAX_WAVE_COMPONENTS).
const MAX_WAVE_COMPONENTS := 1024

## Incremented whenever wave data is regenerated.
var version := 0

# Per-wave data, 8 floats each:
#   (dir.x, dir.z, amplitude, chop_amplitude, omega, phase, phase2, k)
# Sorted by ascending wavelength.
var _waves := PackedFloat32Array()
# Per-LOD wave index ranges into _waves (wave index, not float index).
var _lod_wave_start := PackedInt32Array()
var _lod_wave_end := PackedInt32Array()
# Per-LOD cumulative variance of all shorter waves (foam compensation).
var _lod_cum_variance := PackedFloat32Array()
# Per-LOD median wave number (shallow water attenuation).
var _lod_k_median := PackedFloat32Array()

var _bucket_scale := -1.0
var _dirty := true

var _ssbo: RID
var _compute: CrestRDCompute


func _enter_tree() -> void:
	add_to_group(&"crest_shape_generator")


func _on_spectrum_changed() -> void:
	_dirty = true


func _exit_tree() -> void:
	_free_ssbo()


## Regenerates the wave components from the spectrum (scale independent).
func regenerate(ocean_scale: float, lod_transform: CrestLodTransform) -> void:
	if spectrum == null:
		spectrum = CrestWaveSpectrum.new()
	_dirty = false
	version += 1

	var rng := RandomNumberGenerator.new()
	if random_seed != 0:
		rng.seed = random_seed
	else:
		rng.randomize()

	var wave_data := spectrum.generate_wave_data(components_per_octave, rng)
	var gravity := CrestConstants.GRAVITY * spectrum.gravity_scale

	_waves.resize(0)
	for wd in wave_data:
		var wavelength: float = wd["wavelength"]
		var amp := spectrum.get_amplitude(wavelength, components_per_octave, gravity) * rng.randf() * weight
		if amp < 0.001:
			continue
		var octave := clampi(int(floorf(log(wavelength) / log(2.0))) - CrestWaveSpectrum.SMALLEST_WL_POW_2, 0, CrestWaveSpectrum.NUM_OCTAVES - 1)
		var k := TAU / wavelength
		var angle := deg_to_rad(wd["angle_deg"])
		var dir := Vector2(cos(angle), sin(angle))
		var c := sqrt(wavelength * gravity / TAU)
		var omega := k * c
		var chop_amp := -spectrum.chop_scales[octave] * spectrum.chop * amp
		var phase2 := fmod(wd["phase"] + rng.randf() * TAU * 0.13, TAU)

		_waves.append_array([
			dir.x, dir.y, amp, chop_amp,
			omega, wd["phase"], phase2, k,
		])
		if _waves.size() / 8 >= MAX_WAVE_COMPONENTS:
			break

	# Sort by ascending wavelength so LOD buckets are contiguous ranges.
	_sort_waves_by_wavelength()
	_rebucket(ocean_scale, lod_transform)
	_free_ssbo()
	wave_data_changed.emit()


func _sort_waves_by_wavelength() -> void:
	var count := _waves.size() / 8
	var order: Array[int] = []
	order.resize(count)
	for i in count:
		order[i] = i
	# Descending k = ascending wavelength.
	order.sort_custom(func(a: int, b: int) -> bool:
		return _waves[a * 8 + 7] > _waves[b * 8 + 7])
	var sorted := PackedFloat32Array()
	sorted.resize(_waves.size())
	for i in count:
		for f in 8:
			sorted[i * 8 + f] = _waves[order[i] * 8 + f]
	_waves = sorted


## Re-buckets the sorted waves into per-LOD ranges. Must be redone when the
## ocean scale (or LOD count/resolution) changes.
func _rebucket(ocean_scale: float, lod_transform: CrestLodTransform) -> void:
	_bucket_scale = ocean_scale
	var lod_count := lod_transform.lod_count
	_lod_wave_start.resize(lod_count)
	_lod_wave_end.resize(lod_count)
	_lod_cum_variance.resize(lod_count)
	_lod_k_median.resize(lod_count)

	var count := _waves.size() / 8
	# Waves are sorted by ascending wavelength; buckets are contiguous ranges.
	var wave_idx := 0
	# Drop waves too short for even the smallest cascade (their energy is
	# represented by the detail normal maps instead).
	var min_wl0 := lod_transform.max_wavelength[0] * 0.5
	while wave_idx < count and TAU / _waves[wave_idx * 8 + 7] < min_wl0:
		wave_idx += 1
	var cum_variance := 0.0
	for lod in lod_count:
		var max_wl := lod_transform.max_wavelength[lod]
		_lod_wave_start[lod] = wave_idx
		if lod == lod_count - 1:
			# Largest cascade takes all remaining waves.
			wave_idx = count
		else:
			while wave_idx < count and TAU / _waves[wave_idx * 8 + 7] < max_wl:
				wave_idx += 1
		_lod_wave_end[lod] = wave_idx

		# Median wave number for shallows attenuation.
		_lod_k_median[lod] = TAU / (0.75 * max_wl)
		# Cumulative variance of all waves shorter than this cascade's
		# minimum wavelength (Crest: ShapeGerstner heuristic).
		var min_wl := max_wl * 0.5
		_lod_cum_variance[lod] = cum_variance
		cum_variance += spectrum.chop * spectrum.get_amplitude(1.5 * min_wl, components_per_octave) / (1.5 * min_wl)


## Evaluates the waves into the given wave buffer (RGBA16F texture array,
## one layer per LOD). depth_mgr may be null (disables shallows attenuation).
## Set accumulate=true when another generator already wrote this frame.
func evaluate(wave_buffer: RID, depth_mgr: CrestLodDataMgr, lod_transform: CrestLodTransform, ocean_scale: float, ocean_level: float, time: float, accumulate := false) -> void:
	if _dirty or _waves.is_empty():
		regenerate(ocean_scale, lod_transform)
		if _waves.is_empty():
			return
	if not is_equal_approx(_bucket_scale, ocean_scale):
		_rebucket(ocean_scale, lod_transform)

	var rd := RenderingServer.get_rendering_device()
	if rd == null:
		return
	if _compute == null:
		_compute = CrestRDCompute.from_file(rd, "res://addons/crest/shaders/sim/gerstner_eval.glsl")
		if _compute == null:
			return
	if not _ssbo.is_valid():
		_ssbo = rd.storage_buffer_create(_waves.size() * 4, _waves.to_byte_array())
		if not _ssbo.is_valid():
			push_error("CrestShapeGerstner: failed to create wave SSBO")
			return

	var u_waves := RDUniform.new()
	u_waves.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	u_waves.binding = 0
	u_waves.add_id(_ssbo)

	var u_buffer := RDUniform.new()
	u_buffer.uniform_type = RenderingDevice.UNIFORM_TYPE_IMAGE
	u_buffer.binding = 1
	u_buffer.add_id(wave_buffer)

	var u_depth := RDUniform.new()
	u_depth.uniform_type = RenderingDevice.UNIFORM_TYPE_SAMPLER_WITH_TEXTURE
	u_depth.binding = 2
	if depth_mgr:
		u_depth.add_id(depth_mgr.sampler)
		u_depth.add_id(depth_mgr.current_texture())
	else:
		u_depth.add_id(depth_mgr.sampler if depth_mgr else _get_fallback_sampler(rd))
		u_depth.add_id(_get_fallback_texture(rd))

	var uniform_set := rd.uniform_set_create([u_waves, u_buffer, u_depth], _compute.shader, 0)

	var groups := lod_transform.lod_data_resolution / CrestConstants.THREAD_GROUP_SIZE
	var cl := rd.compute_list_begin()
	rd.compute_list_bind_compute_pipeline(cl, _compute.pipeline)
	rd.compute_list_bind_uniform_set(cl, uniform_set, 0)
	for lod in lod_transform.lod_count:
		var pc := PackedFloat32Array([
			lod_transform.pos_snapped[lod].x, lod_transform.pos_snapped[lod].y,
			lod_transform.texel_width[lod], float(lod_transform.lod_data_resolution),
			float(lod), time, reverse_wave_weight, _lod_cum_variance[lod],
			attenuation_in_shallows if depth_mgr != null else 0.0,
			_lod_k_median[lod], ocean_level,
			float(_lod_wave_start[lod]), float(_lod_wave_end[lod]),
			1.0 if accumulate else 0.0,
		])
		var pc_bytes := CrestRDCompute.pack_push_constants(pc)
		rd.compute_list_set_push_constant(cl, pc_bytes, pc_bytes.size())
		rd.compute_list_dispatch(cl, groups, groups, 1)
	rd.compute_list_end()
	# The uniform set binds per-frame ping-ponged textures, so it is rebuilt
	# every dispatch; freed a few frames later once the GPU is done with it.
	CrestRDCompute.free_rid_deferred(rd, uniform_set)


var _fallback_texture: RID
var _fallback_sampler: RID


func _get_fallback_texture(rd: RenderingDevice) -> RID:
	if not _fallback_texture.is_valid():
		var fmt := RDTextureFormat.new()
		fmt.format = RenderingDevice.DATA_FORMAT_R16G16B16A16_SFLOAT
		fmt.width = 1
		fmt.height = 1
		fmt.array_layers = 1
		fmt.texture_type = RenderingDevice.TEXTURE_TYPE_2D_ARRAY
		fmt.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT | RenderingDevice.TEXTURE_USAGE_CAN_UPDATE_BIT
		_fallback_texture = rd.texture_create(fmt, RDTextureView.new(), [PackedByteArray([0, 0, 0, 0, 0, 0, 0, 0])])
	return _fallback_texture


func _get_fallback_sampler(rd: RenderingDevice) -> RID:
	if not _fallback_sampler.is_valid():
		_fallback_sampler = rd.sampler_create(RDSamplerState.new())
	return _fallback_sampler


func _free_ssbo() -> void:
	var rd := RenderingServer.get_rendering_device()
	if rd:
		if _ssbo.is_valid():
			rd.free_rid(_ssbo)
		if _fallback_texture.is_valid():
			rd.free_rid(_fallback_texture)
		if _fallback_sampler.is_valid():
			rd.free_rid(_fallback_sampler)
	_ssbo = RID()
	_fallback_texture = RID()
	_fallback_sampler = RID()


## CPU mirror of the GPU evaluation, for height queries. Returns the
## displacement at world XZ for waves in LOD buckets up to (and including)
## max_lod, or all waves when max_lod < 0.
func compute_displacement(world_xz: Vector2, time: float, max_lod := -1) -> Vector3:
	var disp := Vector3.ZERO
	var count := _waves.size() / 8
	var end := count if max_lod < 0 else _lod_wave_end[mini(max_lod, _lod_wave_end.size() - 1)]
	for i in end:
		var o := i * 8
		var dir := Vector2(_waves[o], _waves[o + 1])
		var amp := _waves[o + 2]
		var chop_amp := _waves[o + 3]
		var omega := _waves[o + 4]
		var phase := _waves[o + 5]
		var phase2 := _waves[o + 6]
		var k := _waves[o + 7]
		var x := k * dir.dot(world_xz)
		var a1 := x + phase - omega * time
		var a2 := x + phase2 + omega * time
		var s := sin(a1) + reverse_wave_weight * sin(a2)
		disp.y += amp * (cos(a1) + reverse_wave_weight * cos(a2))
		disp.x += chop_amp * s * dir.x
		disp.z += chop_amp * s * dir.y
	return disp
