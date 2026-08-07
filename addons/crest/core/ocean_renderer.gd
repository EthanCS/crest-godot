@tool
class_name CrestOceanRenderer
extends Node3D
## The heart of the ocean system: owns the LOD tile hierarchy, drives the
## per-frame update of all simulations and syncs shader data. Port of
## Crest's OceanRenderer (singleton - one active ocean per scene).

## The currently active ocean (Crest: OceanRenderer.Instance).
static var instance: CrestOceanRenderer

@export_group("LOD")
## Number of LOD cascades (2..15). More cascades = larger viewable area.
@export_range(2, 15) var lod_count := CrestConstants.DEFAULT_LOD_COUNT:
	set(value):
		lod_count = value
		_request_rebuild()
## Resolution of the LOD data textures (per cascade). Multiple of 16.
@export var lod_data_resolution := CrestConstants.DEFAULT_LOD_DATA_RESOLUTION:
	set(value):
		# Crest rounds to an even multiple of 16.
		lod_data_resolution = maxi(16, value - (value % 16))
		_request_rebuild()
## Geometry is this many times coarser than the LOD data resolution.
@export var geometry_down_sample_factor := 2:
	set(value):
		geometry_down_sample_factor = maxi(1, value)
		_request_rebuild()
## Multiplies the outermost skirt to push it towards the horizon.
@export var extents_size_multiplier := 100.0:
	set(value):
		extents_size_multiplier = value
		_request_rebuild()
## Smallest ocean scale (camera near the water).
@export var min_scale := 8.0
## Largest ocean scale (camera high above the water).
@export var max_scale := 256.0

@export_group("Waves")
## Gravity used for wave dispersion.
@export var gravity := CrestConstants.GRAVITY
## Drop detail as the camera rises, scaled by wave height.
@export var drop_detail_height_based_on_waves := true

@export_group("Simulations")
@export var create_foam_sim := true:
	set(value):
		create_foam_sim = value
		_request_rebuild()
@export var create_dynamic_wave_sim := true:
	set(value):
		create_dynamic_wave_sim = value
		_request_rebuild()
@export var create_sea_floor_depth_data := true:
	set(value):
		create_sea_floor_depth_data = value
		_request_rebuild()
@export var create_flow_sim := false:
	set(value):
		create_flow_sim = value
		_request_rebuild()
@export var create_shadow_sim := false:
	set(value):
		create_shadow_sim = value
		_request_rebuild()
@export var create_clip_surface_data := false:
	set(value):
		create_clip_surface_data = value
		_request_rebuild()
@export var create_albedo_data := false:
	set(value):
		create_albedo_data = value
		_request_rebuild()

@export var sim_settings_foam: CrestSimSettingsFoam
@export var sim_settings_wave: CrestSimSettingsWave
@export var sim_settings_flow: CrestSimSettingsFlow
@export var sim_settings_shadow: CrestSimSettingsShadow
@export var sim_settings_clip_surface: CrestSimSettingsClipSurface
@export var sim_settings_albedo: CrestSimSettingsAlbedo

@export_group("Material")
## The ocean surface material. Auto-created from the bundled shader when
## empty; tweak the shader parameters on this material.
@export var ocean_material: ShaderMaterial

@export_group("Frame")
## Node whose XZ position the ocean follows. Defaults to the active camera.
@export var viewpoint: Node3D

## Drives the waves. Not exported (RefCounted); assign from code or leave
## null for the default provider. Set CrestTimeProvider.global_provider to
## drive time globally (cutscenes, network sync).
var time_provider: CrestTimeProvider

## Current power-of-two scale of the ocean (doubles as the camera rises).
var ocean_scale := 8.0
## Scale difference vs last frame as a power-of-two exponent
## (Crest: _CrestLodChange).
var lod_change := 0.0
## Fractional level between scales, for smooth transitions
## (Crest: ViewerAltitudeLevelAlpha).
var viewer_altitude_level_alpha := 0.0
## Smoothed height of the viewer above the water surface.
var viewer_height_above_water := 0.0
## World Y of the sea level.
var ocean_level := 0.0

var lod_transform: CrestLodTransform

# Simulations (null when disabled).
var anim_waves: CrestLodDataMgrAnimWaves
var foam: CrestLodDataMgrFoam
var dyn_waves: CrestLodDataMgrDynWaves
var flow: CrestLodDataMgrFlow
var depth: CrestLodDataMgrSeaFloorDepth
var shadow: CrestLodDataMgrShadow
var clip_surface: CrestLodDataMgrClipSurface
var albedo: CrestLodDataMgrAlbedo

var _cascade_current: RID
var _cascade_source: RID

var _tiles_root: Node3D
var _chunks: Array = []
var _rebuild_queued := false
var _built := false
var _rd_ok := false


func _enter_tree() -> void:
	if Engine.is_editor_hint():
		return
	instance = self
	ocean_level = global_position.y
	if time_provider == null:
		time_provider = CrestTimeProvider.new()
	set_process_priority(100) # run after cameras have moved


func _exit_tree() -> void:
	if instance == self:
		instance = null
	_destroy_ocean()


func _request_rebuild() -> void:
	if not is_inside_tree() or Engine.is_editor_hint():
		return
	_rebuild_queued = true


func _ready() -> void:
	if Engine.is_editor_hint():
		return
	_build_ocean()


func _build_ocean() -> void:
	if _built:
		return
	var rd := RenderingServer.get_rendering_device()
	if rd == null:
		push_warning("CrestOceanRenderer: no RenderingDevice (rendering backend must be Forward+/Mobile). Ocean disabled.")
		return
	_rd_ok = true

	lod_transform = CrestLodTransform.new(lod_count, lod_data_resolution)
	_cascade_current = rd.storage_buffer_create(CrestConstants.CASCADE_PARAMS_COUNT * 8 * 4)
	_cascade_source = rd.storage_buffer_create(CrestConstants.CASCADE_PARAMS_COUNT * 8 * 4)

	anim_waves = CrestLodDataMgrAnimWaves.new()
	anim_waves.init_mgr(lod_data_resolution, lod_count)
	if create_foam_sim:
		foam = CrestLodDataMgrFoam.new()
		foam.init_mgr(lod_data_resolution, lod_count, sim_settings_foam)
	if create_dynamic_wave_sim:
		dyn_waves = CrestLodDataMgrDynWaves.new()
		dyn_waves.init_mgr(lod_data_resolution, lod_count, sim_settings_wave)
	if create_sea_floor_depth_data:
		depth = CrestLodDataMgrSeaFloorDepth.new()
		depth.init_mgr(lod_data_resolution, lod_count)
	if create_flow_sim:
		flow = CrestLodDataMgrFlow.new()
		flow.init_mgr(lod_data_resolution, lod_count, sim_settings_flow)
	if create_shadow_sim:
		shadow = CrestLodDataMgrShadow.new()
		shadow.init_mgr(lod_data_resolution, lod_count, sim_settings_shadow)
	if create_clip_surface_data:
		clip_surface = CrestLodDataMgrClipSurface.new()
		clip_surface.init_mgr(lod_data_resolution, lod_count, sim_settings_clip_surface)
	if create_albedo_data:
		albedo = CrestLodDataMgrAlbedo.new()
		albedo.init_mgr(lod_data_resolution, lod_count, sim_settings_albedo)

	# Initial cascade data (also gives the source buffer valid content).
	lod_transform.update_transforms(ocean_scale, Vector2(global_position.x, global_position.z))
	_upload_cascade_data()

	_build_tiles()
	_built = true


func _build_tiles() -> void:
	if ocean_material == null:
		var shader := load("res://addons/crest/shaders/ocean.gdshader") as Shader
		if shader == null:
			push_error("CrestOceanRenderer: ocean.gdshader not found")
			return
		ocean_material = ShaderMaterial.new()
		ocean_material.shader = shader

	_tiles_root = Node3D.new()
	_tiles_root.name = "CrestTiles"
	add_child(_tiles_root)

	var tile_res := CrestOceanBuilder.get_tile_resolution(lod_data_resolution, geometry_down_sample_factor)
	var extents_mult := extents_size_multiplier * (CrestConstants.MAX_LOD_COUNT + 1 - lod_count)
	var meshes := CrestOceanBuilder.build_patch_meshes(tile_res, extents_mult)
	_chunks.clear()
	for i in lod_count:
		_chunks.append_array(CrestOceanBuilder.create_lod(_tiles_root, i, lod_count, meshes, ocean_material, extents_mult))

	_expand_chunk_bounds()
	_sync_static_material_params()


func _expand_chunk_bounds() -> void:
	var max_vert := 30.0
	var max_horiz := 30.0
	for chunk in _chunks:
		chunk.expand_bounds(max_horiz, max_vert)


func _destroy_ocean() -> void:
	_built = false
	if _tiles_root:
		_tiles_root.queue_free()
		_tiles_root = null
	_chunks.clear()
	for mgr in [anim_waves, foam, dyn_waves, flow, depth, shadow, clip_surface, albedo]:
		if mgr:
			mgr.free_rids()
	anim_waves = null
	foam = null
	dyn_waves = null
	flow = null
	depth = null
	shadow = null
	clip_surface = null
	albedo = null
	var rd := RenderingServer.get_rendering_device()
	if rd:
		if _cascade_current.is_valid():
			rd.free_rid(_cascade_current)
		if _cascade_source.is_valid():
			rd.free_rid(_cascade_source)
	_cascade_current = RID()
	_cascade_source = RID()


func _process(delta: float) -> void:
	if Engine.is_editor_hint():
		return
	if _rebuild_queued:
		_rebuild_queued = false
		if _built:
			_destroy_ocean()
			_build_ocean()
	if not _built or not _rd_ok:
		return
	_run_update(delta)


## Per-frame update, following Crest's OceanRenderer.RunUpdate order.
func _run_update(delta: float) -> void:
	CrestRDCompute.flush_deferred_frees()
	var time := _advance_time(delta)
	var viewer := _get_viewer_position()

	# Follow the viewer on XZ; the root stays at sea level.
	# Precision nudge from Crest (avoids tiles landing on opposite sides of
	# snap boundaries).
	var root_xz := Vector2(viewer.x, viewer.z)
	if absf(fposmod(root_xz.x * 60.0, 1.0)) < 0.003:
		root_xz.x += 0.002
	if absf(fposmod(root_xz.y * 60.0, 1.0)) < 0.003:
		root_xz.y += 0.002
	_tiles_root.global_position = Vector3(root_xz.x, ocean_level, root_xz.y)
	_tiles_root.scale = Vector3(ocean_scale, 1.0, ocean_scale)

	_update_viewer_height(viewer)
	_update_scale()

	lod_transform.update_transforms(ocean_scale, root_xz)
	_upload_cascade_data()

	# Simulations, in Crest's build order:
	# SeaDepths -> Flow -> DynWaves -> AnimWaves -> Foam -> ClipSurface -> Albedo.
	if depth:
		depth.update_sim(lod_transform, _cascade_current, _collect_inputs(&"crest_depth_input"))
	if flow:
		flow.update_sim(lod_transform, _cascade_current, _collect_inputs(&"crest_flow_input"))
	if dyn_waves:
		dyn_waves.update_sim(delta, lod_transform, _cascade_current, _cascade_source,
			flow, depth, ocean_level, gravity, lod_change, _collect_sphere_interactions())
	var shapes := _collect_shape_generators()
	anim_waves.update(shapes, depth, dyn_waves, lod_transform, _cascade_current,
		ocean_scale, ocean_level, time, sim_settings_wave if dyn_waves else null)
	if foam:
		foam.update_sim(delta, lod_transform, _cascade_current, _cascade_source,
			flow, anim_waves, depth, ocean_level, lod_change)
		foam.inject_inputs(lod_transform, _cascade_current, _collect_inputs(&"crest_foam_input"), time)
	if shadow:
		_update_shadow_light()
		shadow.update_sim(delta, lod_transform, _cascade_current, _cascade_source,
			anim_waves, ocean_level, lod_change, time, _collect_shadow_casters())
	if clip_surface:
		clip_surface.update_sim(lod_transform, _cascade_current, _collect_inputs(&"crest_clip_input"))
	if albedo:
		albedo.update_sim(lod_transform, _cascade_current, _collect_inputs(&"crest_albedo_input"))

	_sync_material_params(time)


func _advance_time(delta: float) -> float:
	var provider: CrestTimeProvider = CrestTimeProvider.global_provider if CrestTimeProvider.global_provider else time_provider
	provider.advance(delta)
	return provider.current_time()


func current_time() -> float:
	var provider: CrestTimeProvider = CrestTimeProvider.global_provider if CrestTimeProvider.global_provider else time_provider
	return provider.current_time() if provider else 0.0


func _get_viewer_position() -> Vector3:
	if viewpoint:
		return viewpoint.global_position
	var cam := get_viewport().get_camera_3d()
	if cam:
		return cam.global_position
	return global_position


func _update_viewer_height(viewer_pos: Vector3) -> void:
	var h := viewer_pos.y - ocean_level
	# Teleport detection (Crest: 10 m jump within a short window).
	if absf(h - viewer_height_above_water) > 10.0 and foam:
		foam.notify_teleport()
	viewer_height_above_water = lerpf(viewer_height_above_water, h, 0.05)


## Crest: OceanRenderer.LateUpdateScale - the ocean scale doubles in powers
## of two as the viewer rises above the water.
func _update_scale() -> void:
	var max_vert_disp := 1.0
	var viewer_height := viewer_height_above_water
	if drop_detail_height_based_on_waves:
		viewer_height += max_vert_disp * 0.2
	var cam_distance := maxf(absf(viewer_height) - 4.0, 0.0)
	var level := clampf(cam_distance, min_scale, 1.99 * max_scale)
	var l2 := log(level) / log(2.0)
	var l2f := floorf(l2)
	viewer_altitude_level_alpha = l2 - l2f

	var new_scale := pow(2.0, l2f)
	if not is_equal_approx(new_scale, ocean_scale):
		lod_change = roundf(log(new_scale / ocean_scale) / log(2.0))
		ocean_scale = new_scale
	else:
		lod_change = 0.0


func _upload_cascade_data() -> void:
	var rd := RenderingServer.get_rendering_device()
	if rd == null or not _cascade_current.is_valid():
		return
	var bytes_cur := lod_transform.cascade_data_current.to_byte_array()
	var bytes_src := lod_transform.cascade_data_source.to_byte_array()
	rd.buffer_update(_cascade_current, 0, bytes_cur.size(), bytes_cur)
	rd.buffer_update(_cascade_source, 0, bytes_src.size(), bytes_src)


func cascade_buffer_current() -> RID:
	return _cascade_current


func cascade_buffer_source() -> RID:
	return _cascade_source


func _collect_inputs(group: StringName) -> Array:
	var result := []
	for node in get_tree().get_nodes_in_group(group):
		if node.has_method("get_injection"):
			result.append(node.get_injection())
	return result


func _collect_shape_generators() -> Array:
	var result := []
	for node in get_tree().get_nodes_in_group(&"crest_shape_generator"):
		if node.has_method("evaluate"):
			result.append(node)
	return result


func _collect_sphere_interactions() -> Array:
	var result := []
	for node in get_tree().get_nodes_in_group(&"crest_sphere_interaction"):
		if node.has_method("get_sphere_injection"):
			var d: Dictionary = node.get_sphere_injection()
			if not d.is_empty():
				result.append(d)
	return result


func _collect_shadow_casters() -> Array:
	var result := []
	for node in get_tree().get_nodes_in_group(&"crest_shadow_input"):
		if node.has_method("get_shadow_caster"):
			result.append(node.get_shadow_caster())
	return result


func _update_shadow_light() -> void:
	var light := _find_directional_light()
	if light:
		# Direction towards the light.
		shadow.light_dir = -light.global_transform.basis.z.normalized()


func _find_directional_light() -> DirectionalLight3D:
	for node in get_tree().get_nodes_in_group(&"crest_main_light"):
		if node is DirectionalLight3D:
			return node
	var nodes := get_tree().current_scene.find_children("*", "DirectionalLight3D", true, false) if get_tree().current_scene else []
	if nodes.size() > 0:
		return nodes[0]
	return null


## Max vertical displacement estimate for bounds and scale (m).
func max_vert_displacement() -> float:
	return 30.0


## -- Material sync ---------------------------------------------------------

var _fallback_texture_array: Texture2DArrayRD
var _fallback_texture_2d: Texture2D


func _get_fallback_texture_array() -> Texture2DArrayRD:
	if _fallback_texture_array == null:
		var rd := RenderingServer.get_rendering_device()
		var fmt := RDTextureFormat.new()
		fmt.format = RenderingDevice.DATA_FORMAT_R16G16B16A16_SFLOAT
		fmt.width = 1
		fmt.height = 1
		# Texture2DArrayRD requires more than one layer.
		fmt.array_layers = 2
		fmt.texture_type = RenderingDevice.TEXTURE_TYPE_2D_ARRAY
		fmt.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT | RenderingDevice.TEXTURE_USAGE_CAN_UPDATE_BIT
		var rid := rd.texture_create(fmt, RDTextureView.new(), [PackedByteArray([0, 0, 0, 0, 0, 0, 0, 0]), PackedByteArray([0, 0, 0, 0, 0, 0, 0, 0])])
		_fallback_texture_array = Texture2DArrayRD.new()
		_fallback_texture_array.texture_rd_rid = rid
	return _fallback_texture_array


func _get_fallback_texture_2d() -> Texture2D:
	if _fallback_texture_2d == null:
		var img := Image.create(1, 1, false, Image.FORMAT_RGBAF)
		img.fill(Color(0, 0, 0, 0))
		_fallback_texture_2d = ImageTexture.create_from_image(img)
	return _fallback_texture_2d


func _sync_static_material_params() -> void:
	if ocean_material == null:
		return
	var fallback := _get_fallback_texture_array()
	ocean_material.set_shader_parameter("ld_animated_waves", anim_waves.texture_array)
	ocean_material.set_shader_parameter("ld_foam", foam.texture_array if foam else fallback)
	ocean_material.set_shader_parameter("ld_sea_floor_depth", depth.texture_array if depth else fallback)
	ocean_material.set_shader_parameter("ld_flow", flow.texture_array if flow else fallback)
	ocean_material.set_shader_parameter("ld_shadow", shadow.texture_array if shadow else fallback)
	ocean_material.set_shader_parameter("ld_clip_surface", clip_surface.texture_array if clip_surface else fallback)
	ocean_material.set_shader_parameter("ld_albedo", albedo.texture_array if albedo else fallback)
	ocean_material.set_shader_parameter("slice_count", float(lod_count))
	ocean_material.set_shader_parameter("base_mesh_density", lod_data_resolution * 0.25 / geometry_down_sample_factor)
	# Crest's default foam texture is Foam2.png (foam.png is the legacy one).
	ocean_material.set_shader_parameter("foam_texture", load("res://addons/crest/textures/Foam2.png"))
	ocean_material.set_shader_parameter("normals_texture", load("res://addons/crest/textures/wave_normals.png"))
	ocean_material.set_shader_parameter("caustics_texture", load("res://addons/crest/textures/caustics.png"))
	ocean_material.set_shader_parameter("planar_reflection", _get_fallback_texture_2d())

	_sync_sim_toggles()


func _sync_sim_toggles() -> void:
	if ocean_material == null:
		return
	ocean_material.set_shader_parameter("enable_foam", 1.0 if foam else 0.0)
	ocean_material.set_shader_parameter("enable_shadow", 1.0 if shadow else 0.0)
	ocean_material.set_shader_parameter("enable_clip_surface", 1.0 if clip_surface else 0.0)
	ocean_material.set_shader_parameter("enable_sea_floor_depth", 1.0 if depth else 0.0)
	ocean_material.set_shader_parameter("enable_albedo", 1.0 if albedo else 0.0)


func _sync_material_params(time: float) -> void:
	if ocean_material == null:
		return
	# Cascade data as vec4 uniform arrays (spatial shaders cannot read SSBOs).
	var a: Array[Vector4] = []
	var b: Array[Vector4] = []
	a.resize(CrestConstants.CASCADE_PARAMS_COUNT)
	b.resize(CrestConstants.CASCADE_PARAMS_COUNT)
	var d := lod_transform.cascade_data_current
	for i in CrestConstants.CASCADE_PARAMS_COUNT:
		a[i] = Vector4(d[i * 8 + 0], d[i * 8 + 1], d[i * 8 + 2], d[i * 8 + 3])
		b[i] = Vector4(d[i * 8 + 4], d[i * 8 + 5], d[i * 8 + 6], d[i * 8 + 7])
	ocean_material.set_shader_parameter("cascade_data_a", a)
	ocean_material.set_shader_parameter("cascade_data_b", b)

	var center := _tiles_root.global_position
	ocean_material.set_shader_parameter("ocean_center_pos", center)
	ocean_material.set_shader_parameter("ocean_scale", ocean_scale)
	ocean_material.set_shader_parameter("crest_time", time)
	var base_mesh_density := lod_data_resolution * 0.25 / geometry_down_sample_factor
	var black_point := 0.4 / (base_mesh_density / 8.0)
	ocean_material.set_shader_parameter("lod_alpha_black_point_fade", black_point)
	ocean_material.set_shader_parameter("lod_alpha_black_point_white_point_fade", 1.0 - 2.0 * black_point)
	ocean_material.set_shader_parameter("mesh_scale_lerp", viewer_altitude_level_alpha)
	ocean_material.set_shader_parameter("ocean_level", ocean_level)

	# Lighting: main directional light + environment ambient.
	var light := _find_directional_light()
	if light:
		ocean_material.set_shader_parameter("light_dir", -light.global_transform.basis.z.normalized())
		ocean_material.set_shader_parameter("light_color", light.light_color * light.light_energy)
	var vp := get_viewport()
	if vp and vp.world_3d and vp.world_3d.environment:
		var env := vp.world_3d.environment
		var ambient := env.ambient_light_color * env.ambient_light_energy
		if env.ambient_light_source == Environment.AMBIENT_SOURCE_SKY:
			# Approximate sky ambient with a fixed horizon blend.
			ambient *= 1.0
		ocean_material.set_shader_parameter("ambient_light", ambient)
