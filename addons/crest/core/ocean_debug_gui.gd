@tool
class_name CrestOceanDebugGui
extends Node3D
## Debug overlay that visualises each simulation's cascade texture on a row
## of quads in front of the camera. Port of Crest's OceanDebugGUI (visual
## part). Toggle with F9 at runtime.

## Key that toggles the overlay.
@export var toggle_key := KEY_F9
## Which LOD cascade (slice) to show.
@export_range(0, 14) var slice := 2
## Size of each preview quad in meters at 10 m distance.
@export var quad_size := 1.6

var _overlay: Node3D
var _visible := false


func _unhandled_input(event: InputEvent) -> void:
	if Engine.is_editor_hint():
		return
	if event is InputEventKey and event.pressed and not event.echo and event.keycode == toggle_key:
		set_overlay_visible(not _visible)


func set_overlay_visible(value: bool) -> void:
	_visible = value
	if _visible:
		_rebuild()
	elif _overlay:
		_overlay.queue_free()
		_overlay = null


func _process(_delta: float) -> void:
	if not _visible or _overlay == null:
		return
	var cam := get_viewport().get_camera_3d()
	if cam == null:
		return
	# Keep the row glued in front of the camera.
	var forward := -cam.global_transform.basis.z
	_overlay.global_position = cam.global_position + forward * 10.0 + cam.global_transform.basis.y * 3.0
	_overlay.global_rotation = cam.global_rotation


func _rebuild() -> void:
	if _overlay:
		_overlay.queue_free()
		_overlay = null
	var ocean := CrestOceanRenderer.instance
	if ocean == null:
		return

	var entries: Array = [] # [label, Texture2DArrayRD]
	if ocean.anim_waves:
		entries.append(["anim waves", ocean.anim_waves.texture_array])
	if ocean.foam:
		entries.append(["foam", ocean.foam.texture_array])
	if ocean.dyn_waves:
		entries.append(["dyn waves", ocean.dyn_waves.texture_array])
	if ocean.depth:
		entries.append(["sea depth", ocean.depth.texture_array])
	if ocean.flow:
		entries.append(["flow", ocean.flow.texture_array])
	if ocean.shadow:
		entries.append(["shadow", ocean.shadow.texture_array])
	if ocean.clip_surface:
		entries.append(["clip", ocean.clip_surface.texture_array])
	if ocean.albedo:
		entries.append(["albedo", ocean.albedo.texture_array])
	if entries.is_empty():
		return

	_overlay = Node3D.new()
	_overlay.name = "CrestDebugOverlay"
	add_child(_overlay)

	var sh := Shader.new()
	sh.code = "shader_type spatial; render_mode unshaded, depth_test_disabled, cull_disabled; uniform sampler2DArray arr; uniform float slice; void fragment() { vec3 d = texture(arr, vec3(UV, slice)).rgb; ALBEDO = d * 0.5 + 0.5; }"

	var x := -quad_size * 1.1 * (entries.size() - 1) * 0.5
	for entry in entries:
		var quad := MeshInstance3D.new()
		var qm := QuadMesh.new()
		qm.size = Vector2(quad_size, quad_size)
		quad.mesh = qm
		var mat := ShaderMaterial.new()
		mat.shader = sh
		mat.set_shader_parameter("arr", entry[1])
		mat.set_shader_parameter("slice", float(slice))
		quad.material_override = mat
		quad.position = Vector3(x, 0.0, 0.0)
		_overlay.add_child(quad)

		var label := Label3D.new()
		label.text = entry[0]
		label.pixel_size = 0.02 * quad_size
		label.position = Vector3(x, -quad_size * 0.62, 0.0)
		label.no_depth_test = true
		_overlay.add_child(label)

		x += quad_size * 1.1
