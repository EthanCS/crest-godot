@tool
class_name CrestRegisterFoamInput
extends CrestRegisterLodDataInput
## Injects foam into the foam sim inside the input rect. Port of Crest's
## RegisterFoamInput. Two modes: an additive texture patch (Crest:
## FoamAddFromTex) or an analytic sphere patch with smooth radial falloff.

## Foam amount added per frame (multiplies the texture value in texture
## mode).
@export var strength := 1.0
## Foam mask texture (R channel), stretched across the input rect. Ignored
## in sphere mode.
@export var texture: Texture2D
## true: analytic radial foam patch covering the rect; false: texture patch.
@export var sphere_mode := false


func _enter_tree() -> void:
	add_to_group(&"crest_foam_input")


func get_injection() -> Dictionary:
	return {
		"rect_center": get_rect_center(),
		"rect_half_size": get_rect_half_size(),
		"strength": strength,
		"mode": 1.0 if sphere_mode else 0.0,
		"texture": null if sphere_mode else texture,
	}
