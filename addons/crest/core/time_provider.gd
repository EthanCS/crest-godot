class_name CrestTimeProvider
extends RefCounted
## Provides the time value that drives the waves. Mirrors Crest's
## TimeProvider: the ocean advances on its own clock so it can be paused,
## scaled or driven externally (cutscenes, network sync).

## Global override. When set, every ocean reads time from here. Assign a
## CrestTimeProvider with [member use_custom_time] enabled to drive time
## yourself (e.g. from a cutscene or network clock).
static var global_provider: CrestTimeProvider

var use_custom_time := false
var custom_time := 0.0

## Multiplier applied to the delta supplied by the engine.
var time_scale := 1.0

var paused := false

var _time := 0.0


func advance(delta: float) -> void:
	if paused:
		return
	if use_custom_time:
		_time = custom_time
	else:
		_time += delta * time_scale


func current_time() -> float:
	if use_custom_time:
		return custom_time
	return _time
