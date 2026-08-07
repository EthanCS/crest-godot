@tool
class_name CrestWaveSpectrum
extends Resource
## Wave spectrum model. Port of Crest's OceanWaveSpectrum: 14 octaves from
## 0.0625 m to 512 m wavelength, power in log10 space per octave, with a
## wind-speed dependent (Pierson-Moskowitz style) damping term.

const NUM_OCTAVES := 14
const SMALLEST_WL_POW_2 := -4
const MIN_POWER_LOG := -8.0
const MAX_POWER_LOG := 5.0

## Power (log10) per octave.
@export var power_log := PackedFloat32Array([-5.71, -5.03, -4.54, -3.88, -3.28, -2.32, -1.78, -1.21, -0.54, 0.28, 0.54, 1.03, 1.44, -8.0])

## Per-octave disable flags.
@export var power_disabled: Array[bool] = [false, false, false, false, false, false, false, false, false, false, false, false, false, false]

## Multiplier of the spectrum overall amplitude.
@export var multiplier := 1.0

## Controls horizontal displacement (choppiness).
@export_range(0.0, 4.0) var chop := 1.6

## Variance of wave direction, in degrees (waves spread +/- this around wind direction).
@export_range(0.0, 180.0) var wave_direction_variance := 90.0

## Scales gravity used for wave speed.
@export var gravity_scale := 1.0

## Wind speed in m/s (Crest default 150 km/h = 41.67 m/s).
@export var wind_speed := 41.67

## Wind direction in degrees (0 = +X, rotating towards +Z).
@export var wind_direction_angle := 0.0

## Per-octave chop scales.
@export var chop_scales := PackedFloat32Array([1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0])

## Per-octave gravity scales.
@export var gravity_scales := PackedFloat32Array([1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0])


static func small_wavelength(octave: int) -> float:
	return pow(2.0, SMALLEST_WL_POW_2 + octave)


## Amplitude for one wave component of the given wavelength.
## Port of OceanWaveSpectrum.GetAmplitude.
func get_amplitude(wavelength: float, components_per_octave: int, gravity := CrestConstants.GRAVITY) -> float:
	if wavelength <= 0.0:
		return 0.0
	var wl_pow2 := clampf(log(wavelength) / log(2.0), SMALLEST_WL_POW_2, SMALLEST_WL_POW_2 + NUM_OCTAVES - 1.0)
	var wl_floor := floorf(wl_pow2)
	var index := int(wl_floor) - SMALLEST_WL_POW_2
	var lambda_lo := pow(2.0, wl_floor)
	var alpha := (wavelength - lambda_lo) / lambda_lo

	var this_power: float = MIN_POWER_LOG if power_disabled[index] else power_log[index]
	var next_idx := mini(index + 1, NUM_OCTAVES - 1)
	var next_power: float = MIN_POWER_LOG if power_disabled[next_idx] else power_log[next_idx]
	var power := pow(10.0, lerpf(this_power, next_power, alpha))

	# Deep water dispersion: omega = sqrt(g * k).
	var k := TAU / wavelength
	var omega_lo := sqrt(gravity * TAU / lambda_lo)
	var omega_hi := sqrt(gravity * TAU / (2.0 * lambda_lo))
	var domega := (omega_lo - omega_hi) / components_per_octave

	# Wind influence (alpha-beta spectrum beta term).
	var wm := 0.87 * gravity / maxf(wind_speed, 0.01)
	var w := sqrt(gravity * k)
	power *= exp(-1.291 * pow(wm / w, 4.0))

	var a := sqrt(2.0 * power * domega)
	# "Gerstner fudge" so Gerstner and FFT amplitudes visually match.
	a *= 5.0
	return a * multiplier


## Samples wavelengths/directions/phases for wave components, stratified
## per octave. Port of OceanWaveSpectrum.GenerateWaveData.
## Returns an array of dictionaries: { wavelength, angle_deg, phase }.
func generate_wave_data(components_per_octave: int, rng: RandomNumberGenerator) -> Array[Dictionary]:
	var result: Array[Dictionary] = []
	var min_wavelength := pow(2.0, SMALLEST_WL_POW_2)
	for octave in NUM_OCTAVES:
		for i in components_per_octave:
			var wl_min := min_wavelength * (1.0 + float(i) / components_per_octave)
			var wl_max := minf(min_wavelength * (1.0 + float(i + 1) / components_per_octave), 2.0 * min_wavelength)
			var wavelength := lerpf(wl_min, wl_max, rng.randf())
			var rnd := (float(i) + rng.randf()) / components_per_octave
			var angle_deg := (2.0 * rnd - 1.0) * wave_direction_variance + wind_direction_angle
			var phase := TAU * (float(i) + rng.randf()) / components_per_octave
			result.append({
				"wavelength": wavelength,
				"angle_deg": angle_deg,
				"phase": phase,
			})
		min_wavelength *= 2.0
	return result


## Applies the Pierson-Moskowitz model to the octave powers (editor utility).
## Port of OceanWaveSpectrum.ApplyPhillipsSpectrum.
func apply_phillips_spectrum(p_wind_speed: float, gravity := CrestConstants.GRAVITY) -> void:
	for octave in NUM_OCTAVES:
		var wl := small_wavelength(octave)
		var omega := sqrt(gravity * TAU / wl)
		var s := 8.1e-3 * gravity * gravity / pow(omega, 5.0)
		power_log[octave] = maxf(log(s) / log(10.0), MIN_POWER_LOG)
	wind_speed = p_wind_speed
	emit_changed()


## Total statistical variance of the spectrum (used for foam compensation).
func compute_cumulative_variance(weights: PackedFloat32Array) -> float:
	# Heuristic from Crest's ShapeGerstner: horizontal displacement ~ amp*chop,
	# normalised by wavelength.
	var total := 0.0
	for i in weights.size():
		total += weights[i]
	return total
