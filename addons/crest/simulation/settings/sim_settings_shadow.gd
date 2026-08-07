@tool
class_name CrestSimSettingsShadow
extends Resource
## Port of Crest's SimSettingsShadow. The "blur" is achieved by jittering
## sample positions each frame and accumulating with an exponential moving
## average.

## Diameter of the soft shadow jitter (m).
@export var jitter_diameter_soft := 15.0

## Responsiveness of the soft shadow accumulation.
@export_range(0.0, 1.0) var current_frame_weight_soft := 0.03

## Diameter of the hard shadow jitter (m).
@export var jitter_diameter_hard := 0.6

## Responsiveness of the hard shadow accumulation.
@export_range(0.0, 1.0) var current_frame_weight_hard := 0.15
