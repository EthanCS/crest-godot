@tool
class_name CrestSimSettingsAnimatedWaves
extends Resource
## Port of Crest's SimSettingsAnimatedWaves.

## How much waves are dampened in shallow water.
@export_range(0.0, 1.0) var attenuation_in_shallows := 0.95

## Depth below which attenuation no longer applies (performance hint).
@export var maximum_attenuation_depth := 1000.0

## Collision readback mode note: queries use analytic Gerstner evaluation or
## GPU readback depending on the active shape generators.
@export var collision_source := 0
