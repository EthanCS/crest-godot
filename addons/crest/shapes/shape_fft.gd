@tool
class_name CrestShapeFFT
extends Node3D
## FFT-based wave generator. Port of Crest's ShapeFFT: evaluates the wave
## spectrum on the GPU via IFFT into tiled displacement textures, which are
## then sampled into the view-following animated-waves cascades.

@export var spectrum: CrestWaveSpectrum:
	set(value):
		if spectrum and spectrum.changed.is_connected(_on_spectrum_changed):
			spectrum.changed.disconnect(_on_spectrum_changed)
		spectrum = value
		if spectrum and not spectrum.changed.is_connected(_on_spectrum_changed):
			spectrum.changed.connect(_on_spectrum_changed)
		_spectrum_dirty = true

## FFT resolution per cascade (power of two, 16..512).
@export var resolution := 128:
	set(value):
		resolution = clampi(value, 16, 512)
		_spectrum_dirty = true
		_reinit_needed = true

## Overall weight of this wave generator.
@export_range(0.0, 1.0) var weight := 1.0

## Turbulence: blends directional spreading towards uniform.
@export_range(0.0, 1.0) var turbulence := 0.145:
	set(value):
		turbulence = value
		_spectrum_dirty = true

## Random seed for the spectrum phases.
@export var random_seed := 0:
	set(value):
		random_seed = value
		_spectrum_dirty = true

var _fft: CrestFFTCompute
var _spectrum_dirty := true
var _reinit_needed := true
var _slice_map_buffer: RID
var _last_scale := -1.0


func _enter_tree() -> void:
	add_to_group(&"crest_shape_generator")


func _exit_tree() -> void:
	if _fft:
		_fft.free_rids()
	_fft = null
	var rd := RenderingServer.get_rendering_device()
	if rd and _slice_map_buffer.is_valid():
		rd.free_rid(_slice_map_buffer)
	_slice_map_buffer = RID()


func _on_spectrum_changed() -> void:
	_spectrum_dirty = true


var _debug_printed := false


## Shape generator interface used by CrestLodDataMgrAnimWaves.
func evaluate(wave_buffer: RID, _depth_mgr: CrestLodDataMgr, lod_transform: CrestLodTransform, ocean_scale: float, _ocean_level: float, time: float, accumulate := false) -> void:
	var rd := RenderingServer.get_rendering_device()
	if rd == null:
		return
	if not _debug_printed:
		_debug_printed = true
		print("CrestShapeFFT: evaluate called, time=", time)
	if spectrum == null:
		spectrum = CrestWaveSpectrum.new()
	if _reinit_needed or _fft == null:
		if _fft:
			_fft.free_rids()
		_fft = CrestFFTCompute.new()
		if not _fft.initialize(resolution):
			_fft = null
			return
		_reinit_needed = false
		_spectrum_dirty = true
		_last_scale = -1.0

	if _spectrum_dirty:
		var wind_angle := deg_to_rad(spectrum.wind_direction_angle)
		_fft.rebuild_spectrum(spectrum, Vector2(cos(wind_angle), sin(wind_angle)), turbulence, float(random_seed))
		_spectrum_dirty = false

	_fft.advance_time(time * spectrum.gravity_scale, spectrum.gravity_scale, spectrum.chop)

	# Map each LOD cascade to the FFT cascade whose band covers its
	# wavelengths. Rebuilt when the ocean scale changes.
	if not is_equal_approx(_last_scale, ocean_scale):
		_last_scale = ocean_scale
		var map := PackedInt32Array()
		map.resize(lod_transform.lod_count)
		for lod in lod_transform.lod_count:
			var max_wl := lod_transform.max_wavelength[lod]
			map[lod] = clampi(int(roundf(log(8.0 * max_wl) / log(2.0))), 0, CrestFFTCompute.CASCADE_COUNT - 1)
		if not _slice_map_buffer.is_valid():
			_slice_map_buffer = rd.storage_buffer_create(CrestConstants.CASCADE_PARAMS_COUNT * 4)
		rd.buffer_update(_slice_map_buffer, 0, map.size() * 4, map.to_byte_array())

	# Variance of waves shorter than each cascade (foam compensation is
	# approximated with the spectrum-wide value here).
	var variance := spectrum.chop * spectrum.get_amplitude(1.5 * lod_transform.max_wavelength[0] * 0.5, 8) / (1.5 * lod_transform.max_wavelength[0] * 0.5)
	_fft.sample_into_wave_buffer(wave_buffer, CrestOceanRenderer.instance.cascade_buffer_current(), _slice_map_buffer, lod_transform, accumulate, weight, variance)
