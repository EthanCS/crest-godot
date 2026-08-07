@tool
class_name CrestOceanPlanarReflection
extends Node3D
## Generates a planar reflection texture for the ocean surface. Port of
## Crest's OceanPlanarReflection.
##
## Every [member refresh_per_frames] frames the scene is rendered with a
## camera mirrored about the water plane (y = ocean level + clip offset) and
## the result is fed into the ocean material's [code]planar_reflection[/code]
## sampler, which the ocean shader samples at [code]SCREEN_UV[/code].
##
## Differences vs Crest:
## - Godot's Camera3D has no oblique near-plane projection, so geometry
##   below the water plane is not clipped away and can leak into the
##   reflection. [member clip_plane_offset] only shifts the mirror plane.
## - A Camera3D transform must stay right-handed, so the mirrored camera
##   flips the image horizontally; a tiny 2D blit viewport flips it back
##   (Crest instead renders with a negative-determinant view matrix and
##   inverts face culling).
## - The ocean tiles are excluded by moving them to a render layer outside
##   [member cull_mask] for the duration of the reflection draw. Both
##   viewports share one World3D, so toggling [code]visible[/code] would
##   also hide the ocean from the main camera.

## Resolution of the reflection texture. The height is adjusted at runtime
## to match the main viewport aspect ratio so the reflection lines up with
## the screen when sampled at [code]SCREEN_UV[/code]; the width is used
## as-is.
@export var reflection_texture_size := Vector2i(512, 512):
	set(value):
		reflection_texture_size = Vector2i(maxi(16, value.x), maxi(16, value.y))
		_apply_size()
## Refresh the reflection every N frames (1 = every frame).
@export_range(1, 60) var refresh_per_frames := 1:
	set(value):
		refresh_per_frames = maxi(1, value)
## Render layers visible to the reflection camera. The ocean tiles are
## always excluded, regardless of this mask.
@export_flags_3d_render var cull_mask := 1:
	set(value):
		cull_mask = value
		if _camera:
			_camera.cull_mask = value
## Raises the mirror plane, reducing artifacts right at the waterline.
## (Crest also feeds this into its oblique near-plane clip, which Godot's
## Camera3D cannot express.)
@export var clip_plane_offset := 0.07
## Strength of the reflection on the water surface
## ([code]planar_reflection_intensity[/code] shader parameter). Reset to 0
## when this node leaves the tree.
@export_range(0.0, 1.0) var intensity := 1.0

var _render_viewport: SubViewport
var _output_viewport: SubViewport
var _camera: Camera3D
var _flip_sprite: Sprite2D
var _frame := 0
# Chunks currently hidden from the reflection pass -> their original layers.
var _hidden_chunks: Dictionary = {}


func _ready() -> void:
	if Engine.is_editor_hint():
		return
	set_process_priority(90) # run after the main camera has moved
	# 3D pass: renders the shared world with the mirrored camera.
	_render_viewport = SubViewport.new()
	_render_viewport.name = "CrestReflectionRender"
	_render_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
	add_child(_render_viewport, false, Node.INTERNAL_MODE_BACK)
	_camera = Camera3D.new()
	_camera.name = "ReflectionCamera"
	_camera.cull_mask = cull_mask
	_render_viewport.add_child(_camera)
	# 2D pass: flips the rendered image horizontally so it matches the true
	# mirror image (see the class doc).
	_output_viewport = SubViewport.new()
	_output_viewport.name = "CrestReflectionOutput"
	_output_viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	add_child(_output_viewport, false, Node.INTERNAL_MODE_BACK)
	_flip_sprite = Sprite2D.new()
	_flip_sprite.name = "Flip"
	_flip_sprite.flip_h = true
	_flip_sprite.texture = _render_viewport.get_texture()
	_output_viewport.add_child(_flip_sprite)
	_apply_size()


func _exit_tree() -> void:
	_restore_chunks()
	var ocean := CrestOceanRenderer.instance
	if ocean and ocean.ocean_material:
		ocean.ocean_material.set_shader_parameter("planar_reflection_intensity", 0.0)


func _process(_delta: float) -> void:
	if _render_viewport == null:
		return
	var ocean := CrestOceanRenderer.instance
	if ocean == null or ocean.ocean_material == null:
		return
	# Re-assert every frame: the ocean pushes its own material parameters
	# (including a fallback reflection texture) whenever it rebuilds.
	var mat := ocean.ocean_material
	mat.set_shader_parameter("planar_reflection", _output_viewport.get_texture())
	mat.set_shader_parameter("planar_reflection_intensity", intensity)
	if intensity <= 0.001:
		return
	_frame += 1
	if _frame % refresh_per_frames != 0:
		return
	var vp := get_viewport()
	if vp == null:
		return
	var cam := vp.get_camera_3d()
	if cam == null:
		return
	_update_reflection_camera(cam, ocean.ocean_level + clip_plane_offset)
	_apply_size()
	_render_once(ocean)


func _render_once(ocean: CrestOceanRenderer) -> void:
	# Exclude the ocean surface from the reflection pass: move the tiles to
	# a layer outside the reflection camera's cull mask for a single draw
	# (visible = false would also hide the ocean from the main camera, as
	# both viewports share one World3D).
	var hidden_bit := _hidden_layer_bit()
	for chunk in ocean._chunks:
		if chunk is VisualInstance3D and is_instance_valid(chunk):
			_hidden_chunks[chunk] = chunk.layers
			chunk.layers = hidden_bit
	_render_viewport.render_target_update_mode = SubViewport.UPDATE_ONCE
	await RenderingServer.frame_post_draw
	_restore_chunks()


func _restore_chunks() -> void:
	for chunk in _hidden_chunks:
		if is_instance_valid(chunk):
			chunk.layers = _hidden_chunks[chunk]
	_hidden_chunks.clear()


# A render layer bit outside the reflection camera's cull mask. The main
# camera uses the default all-layers mask, so the ocean stays visible to it.
func _hidden_layer_bit() -> int:
	var free_bits := (~cull_mask) & 0xFFFFF
	if free_bits == 0:
		return 1 << 19
	return free_bits & -free_bits


func _update_reflection_camera(cam: Camera3D, plane_y: float) -> void:
	_camera.projection = cam.projection
	_camera.fov = cam.fov
	_camera.size = cam.size
	_camera.frustum_offset = cam.frustum_offset
	_camera.near = cam.near
	_camera.far = cam.far
	var t := cam.global_transform
	var origin := t.origin
	origin.y = 2.0 * plane_y - origin.y
	# Reflect the basis across the horizontal plane (negate Y of each basis
	# vector), then negate the right vector to keep the basis right-handed.
	# This flips the rendered image horizontally; the output viewport flips
	# it back so the ocean shader can sample it directly at SCREEN_UV.
	var b := t.basis
	var refl := Basis(
		Vector3(-b.x.x, b.x.y, -b.x.z),
		Vector3(b.y.x, -b.y.y, b.y.z),
		Vector3(b.z.x, -b.z.y, b.z.z))
	_camera.global_transform = Transform3D(refl, origin)


func _apply_size() -> void:
	if _render_viewport == null:
		return
	var target_size := reflection_texture_size
	var vp := get_viewport()
	if vp:
		var vs := vp.get_visible_rect().size
		if vs.x > 0.0 and vs.y > 0.0:
			# Match the main viewport aspect ratio so the reflection aligns
			# with the screen when sampled at SCREEN_UV.
			target_size.y = maxi(16, roundi(float(target_size.x) * vs.y / vs.x))
	if _render_viewport.size == target_size:
		return
	_render_viewport.size = target_size
	_output_viewport.size = target_size
	_flip_sprite.position = Vector2(target_size) * 0.5
