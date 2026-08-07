@tool
class_name CrestSimSettingsFoam
extends Resource
## Port of Crest's SimSettingsFoam.

## Gradual fade out rate of foam (1/s).
@export var foam_fade_rate := 0.8

## Scales intensity of foam generated from waves.
@export_range(0.0, 5.0) var wave_foam_strength := 1.0

## How much of the waves generate foam (higher = more foam).
@export_range(0.0, 1.0) var wave_foam_coverage := 0.55

## Minimum animated-waves slice the foam samples; increase to filter out
## large swells generating excess foam near shore.
@export_range(0, 14) var filter_waves := 0

## Maximum depth that shoreline foam gets generated for (m).
@export var shoreline_foam_max_depth := 0.65

## Scales intensity of foam generated in shallow water.
@export_range(0.0, 5.0) var shoreline_foam_strength := 2.0

## Simulation update frequency (Hz).
@export var simulation_frequency := 30.0

## Pre-fill the sim on the first frame / after teleports.
@export var prewarm := true
