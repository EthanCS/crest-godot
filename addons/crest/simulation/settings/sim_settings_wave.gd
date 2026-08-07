@tool
class_name CrestSimSettingsWave
extends Resource
## Port of Crest's SimSettingsWave (dynamic waves sim).

## Simulation update frequency (Hz).
@export var simulation_frequency := 60.0

## Energy dissipation rate per second.
@export_range(0.0, 1.0) var damping := 0.05

## Stability (CFL) number: caps wave speed for numerical stability.
@export_range(0.0, 1.0) var courant_number := 0.7

## How much waves dampen in shallow water.
@export_range(0.0, 1.0) var attenuation_in_shallows := 1.0

## Horizontal displacement sharpening when combining into animated waves.
@export_range(0.0, 32.0) var horiz_displace := 3.0

## Clamp for horizontal displacement (multiple of texel width).
@export_range(0.0, 1.0) var displace_clamp := 0.3

## Gravity multiplier for wave speed.
@export var gravity_multiplier := 1.0
