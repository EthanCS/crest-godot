@tool
class_name CrestOceanDepthCache
extends Node3D
## Renders the terrain height around this node into a cache texture that
## feeds the sea floor depth sim. Port of Crest's OceanDepthCache.
##
## Two height sources:
## - heightmap_texture (static path): the texture is fed to the depth sim
##   directly; R channel = world height minus this node's Y. No rendering.
## - Scene capture: a hidden SubViewport with a top-down orthographic camera
##   renders the GeometryInstance3D nodes matching [member layers] with an
##   override material that encodes world Y into the R channel
##   (depth_cache_height.gdshader). The frame is read back into an
##   ImageTexture that already stores world heights.
##
## Capture limitations:
## - For one frame the captured geometry renders with a flat override
##   material; this also affects the main viewport (single-frame flicker).
## - The CPU readback stalls the GPU pipeline; REALTIME is expensive and
##   runs one frame behind.
## - Requires a floating point 3D colour buffer (Forward+/Mobile) so the
##   height survives in the R channel (half float precision).
## - Crest ocean chunks are skipped automatically, but keep terrain on its
##   own render layer and select only that in [member layers].
## - Capture runs at runtime only (no editor preview).

enum RefreshMode {
	REALTIME, ## Re-capture every frame (expensive, see class doc).
	ON_START, ## Capture once when the scene starts.
	ON_DEMAND, ## Capture only when populate_cache() is called.
}

## Longest side of the capture texture in pixels.
@export var resolution := 512
## World XZ area covered by the cache, centred on this node.
@export var cache_size := Vector2(256.0, 256.0)
## Height above the node at which the capture camera sits, looking down.
## Terrain above this altitude is clipped by the near plane; terrain deeper
## than 1000 m below the node falls back to the depth baseline.
@export var camera_max_terrain_height := 100.0
## When the scene capture re-runs (ignored when heightmap_texture is set).
@export var refresh_mode := RefreshMode.ON_START
## Render layers captured into the cache (cull mask of the capture camera).
@export_flags_3d_render var layers := 1
## Static heightmap (R = world height minus node Y). When set, no scene
## capture is performed and this texture is used as the depth input.
@export var heightmap_texture: Texture2D

var _cache_texture: ImageTexture

var _viewport: SubViewport
var _camera: Camera3D
var _height_material: ShaderMaterial
var _capture_in_flight := false


func _enter_tree() -> void:
	add_to_group(&"crest_depth_input")


func _ready() -> void:
	if Engine.is_editor_hint():
		return
	if refresh_mode == RefreshMode.ON_START:
		populate_cache()


func _process(_delta: float) -> void:
	if Engine.is_editor_hint():
		return
	if refresh_mode == RefreshMode.REALTIME:
		populate_cache()


## Depth sim input, collected every frame by CrestOceanRenderer.
func get_injection() -> Dictionary:
	var use_heightmap := heightmap_texture != null
	return {
		"rect_center": Vector2(global_position.x, global_position.z),
		"rect_half_size": cache_size * 0.5,
		"texture": heightmap_texture if use_heightmap else _cache_texture,
		# The capture material writes world heights directly; the heightmap
		# path stores heights relative to the node Y.
		"height_offset": global_position.y if use_heightmap else 0.0,
		"sea_level_offset": 0.0,
		"mode": 0.0,
	}


## Captures the terrain height once and updates the cache texture. Async:
## completes after the next drawn frame. Calls made while a capture is in
## flight are dropped. Runtime only.
func populate_cache() -> void:
	if Engine.is_editor_hint() or heightmap_texture != null or _capture_in_flight:
		return
	_capture_in_flight = true
	_ensure_capture_rig()

	# Render the terrain with the height-encoding override material for one
	# frame, then restore.
	var overridden := []
	for geometry in _collect_terrain_geometry():
		overridden.append([geometry, geometry.material_override])
		geometry.material_override = _height_material
	_viewport.render_target_update_mode = SubViewport.UPDATE_ONCE
	await RenderingServer.frame_post_draw
	for pair in overridden:
		if is_instance_valid(pair[0]):
			pair[0].material_override = pair[1]
	_capture_in_flight = false

	if not is_inside_tree():
		return
	var img := _viewport.get_texture().get_image()
	if img == null or img.is_empty():
		push_warning("CrestOceanDepthCache: capture failed, no image read back.")
		return
	img.convert(Image.FORMAT_RF)
	_recycle_previous_texture()
	_cache_texture = ImageTexture.create_from_image(img)


func _ensure_capture_rig() -> void:
	if _viewport == null:
		_viewport = SubViewport.new()
		_viewport.name = "CrestDepthCacheViewport"
		_viewport.gui_disable_input = true
		_viewport.handle_input_locally = false
		# Renders only when a capture requests it. Shares the World3D of the
		# main viewport (own_world_3d stays false).
		_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
		_camera = Camera3D.new()
		_camera.name = "CrestDepthCacheCamera"
		_camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		_camera.current = true
		# Data pass: background = depth baseline, no tonemapping or effects
		# so the R channel survives as raw height data.
		var env := Environment.new()
		env.background_mode = Environment.BG_COLOR
		env.background_color = Color(CrestConstants.OCEAN_DEPTH_BASELINE, 0.0, 0.0, 1.0)
		env.tonemap_mode = Environment.TONE_MAPPER_LINEAR
		env.glow_enabled = false
		env.fog_enabled = false
		env.volumetric_fog_enabled = false
		env.ssao_enabled = false
		env.ssil_enabled = false
		env.ssr_enabled = false
		env.sdfgi_enabled = false
		_camera.environment = env
		_viewport.add_child(_camera)
		add_child(_viewport)

		_height_material = ShaderMaterial.new()
		_height_material.shader = load("res://addons/crest/shaders/depth_cache_height.gdshader")

	# Keep the rig in sync with the node and exports.
	var size_x := maxf(cache_size.x, 0.01)
	var size_y := maxf(cache_size.y, 0.01)
	var longest := maxf(size_x, size_y)
	# Ortho size is the FULL vertical extent (keep_aspect = KEEP_HEIGHT), so
	# size = cache_size.y; giving the viewport the cache aspect makes the
	# horizontal extent come out at exactly cache_size.x.
	_viewport.size = Vector2i(
		maxi(1, roundi(resolution * size_x / longest)),
		maxi(1, roundi(resolution * size_y / longest)))
	_camera.size = size_y
	_camera.near = 0.05
	_camera.far = camera_max_terrain_height + 1000.0
	_camera.cull_mask = layers
	# Top-down, one camera_max_terrain_height above the node. With this
	# rotation the image top maps to world -Z and right to +X, matching the
	# inject shader's uv mapping.
	_camera.position = global_position + Vector3(0.0, camera_max_terrain_height, 0.0)
	_camera.rotation = Vector3(-PI * 0.5, 0.0, 0.0)


func _collect_terrain_geometry() -> Array:
	var result := []
	var tree := get_tree()
	if tree == null or tree.root == null:
		return result
	for geometry in tree.root.find_children("*", "GeometryInstance3D", true, false):
		if (geometry.layers & layers) == 0:
			continue
		if geometry is CrestOceanChunkRenderer:
			continue
		result.append(geometry)
	return result


## The depth manager caches an RD copy of every input texture keyed by
## Texture2D instance id; without this the old copy would leak on every
## capture (and a reused instance would never re-upload). Frees the stale
## GPU copy deferred, in case this frame's dispatch still references it.
func _recycle_previous_texture() -> void:
	if _cache_texture == null:
		return
	var ocean := CrestOceanRenderer.instance
	if ocean == null or ocean.depth == null:
		return
	var cache: Variant = ocean.depth.get("_tex_cache")
	if not cache is Dictionary:
		return
	var key := _cache_texture.get_instance_id()
	if cache.has(key):
		var rd := RenderingServer.get_rendering_device()
		if rd != null:
			CrestRDCompute.free_rid_deferred(rd, cache[key])
		cache.erase(key)
