@tool
class_name CrestUnderwaterRenderer
extends Node3D
## Underwater post-processing (fog + meniscus). Simplified port of Crest's
## UnderwaterRenderer full-screen effect.
##
## Each frame the camera height is checked against the water surface with
## CrestCollision.sample_height. While underwater, a full-screen ColorRect on
## a high CanvasLayer applies underwater.gdshader: Beer-Lambert fog towards
## Crest's scatter colour, plus a darkened meniscus band at the waterline
## when the camera is near the surface. The effect fades in/out over
## FADE_TIME when crossing the surface.
##
## Simplifications vs Crest: no ocean mask/stencil buffer (the overlay is
## full-screen whenever the camera is underwater), no underwater curtain
## geometry, no caustics on submerged scene geometry, and the meniscus is an
## analytic band around the projected horizon instead of a 3-sample mask
## check along the horizon normal.

const UNDERWATER_SHADER: Shader = preload("res://addons/crest/shaders/underwater.gdshader")
## Seconds to fade the effect in/out when crossing the surface.
const FADE_TIME := 0.2
## Camera distance from the surface (m) within which the meniscus shows.
const MENISCUS_RANGE := 2.0
## Crest default _DepthFogDensity (matches ocean.gdshader).
const BASE_DEPTH_FOG_DENSITY := Vector3(0.9, 0.3, 0.35)

## Master switch. When false the overlay stays hidden.
@export var enabled := true
## Draw the darkened meniscus band at the waterline near the surface.
@export var meniscus_enabled := true
## Multiplies the fog density (Crest: UnderwaterRenderer.DepthFogDensityFactor).
@export var depth_fog_density_factor := 1.0
## Camera to track. Defaults to the active camera of this node's viewport.
@export var camera: Camera3D

var _layer: CanvasLayer
var _rect: ColorRect
var _material: ShaderMaterial
var _fade := 0.0


func _ready() -> void:
	if Engine.is_editor_hint():
		return
	_build_overlay()
	# Run after CrestOceanRenderer (100) so wave data is current.
	set_process_priority(200)


func _build_overlay() -> void:
	_material = ShaderMaterial.new()
	_material.shader = UNDERWATER_SHADER

	_rect = ColorRect.new()
	_rect.name = "CrestUnderwaterRect"
	_rect.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_rect.material = _material
	_rect.visible = false

	_layer = CanvasLayer.new()
	_layer.name = "CrestUnderwaterLayer"
	_layer.layer = 128
	_layer.add_child(_rect)
	add_child(_layer)


func _process(delta: float) -> void:
	if Engine.is_editor_hint() or _rect == null:
		return

	var cam := camera
	if cam == null:
		cam = get_viewport().get_camera_3d()

	var underwater := false
	var water_height := 0.0
	if enabled and cam != null:
		var out := [0.0]
		if CrestCollision.sample_height(Vector2(cam.global_position.x, cam.global_position.z), out):
			water_height = out[0]
			underwater = cam.global_position.y < water_height

	_fade = move_toward(_fade, 1.0 if underwater else 0.0, delta / FADE_TIME)
	_rect.visible = _fade > 0.001
	if _rect.visible:
		_sync_shader(cam, water_height)


func _sync_shader(cam: Camera3D, water_height: float) -> void:
	_material.set_shader_parameter("effect_strength", _fade)
	_material.set_shader_parameter("depth_fog_density", BASE_DEPTH_FOG_DENSITY * depth_fog_density_factor)
	_material.set_shader_parameter("inv_projection_matrix", cam.get_camera_projection().inverse())
	_material.set_shader_parameter("camera_basis", cam.global_transform.basis)

	# Lighting: same values CrestOceanRenderer feeds the ocean material.
	var light := _find_directional_light()
	if light:
		_material.set_shader_parameter("light_dir", -light.global_transform.basis.z.normalized())
		_material.set_shader_parameter("light_color", light.light_color * light.light_energy)
	var vp := cam.get_viewport()
	if vp and vp.world_3d and vp.world_3d.environment:
		var env := vp.world_3d.environment
		_material.set_shader_parameter("ambient_light", env.ambient_light_color * env.ambient_light_energy)

	# Meniscus: project the ocean horizon to a screen-space line.
	var meniscus := 0.0
	var waterline_y := -1.0
	if meniscus_enabled:
		var height_above := cam.global_position.y - water_height
		meniscus = clampf(1.0 - absf(height_above) / MENISCUS_RANGE, 0.0, 1.0)
		if meniscus > 0.001:
			var forward := -cam.global_transform.basis.z
			var flat := Vector3(forward.x, 0.0, forward.z)
			# No horizon on screen when looking straight up/down.
			if flat.length() > 0.01 and vp:
				# A far point in the horizontal forward direction projects to
				# the waterline (valid while the camera is near the surface).
				var p := cam.global_position + flat.normalized() * 1.0e5
				if not cam.is_position_behind(p):
					var vp_height := vp.get_visible_rect().size.y
					if vp_height > 0.0:
						waterline_y = cam.unproject_position(p).y / vp_height
	_material.set_shader_parameter("meniscus_strength", meniscus)
	_material.set_shader_parameter("waterline_screen_y", waterline_y)


func _find_directional_light() -> DirectionalLight3D:
	for node in get_tree().get_nodes_in_group(&"crest_main_light"):
		if node is DirectionalLight3D:
			return node
	var nodes := get_tree().current_scene.find_children("*", "DirectionalLight3D", true, false) if get_tree().current_scene else []
	if nodes.size() > 0:
		return nodes[0]
	return null
