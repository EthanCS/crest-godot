class_name CrestLodDataMgrClipSurface
extends CrestLodDataMgr
## Clip surface: removes the ocean surface in registered areas (e.g. inside
## boat hulls, docks). Port of Crest's LodDataMgrClipSurface.

var settings := CrestSimSettingsClipSurface.new()

var _clear: CrestRDCompute
var _inject: CrestRDCompute


func init_mgr(p_resolution: int, p_layers: int, p_settings: CrestSimSettingsClipSurface) -> void:
	settings = p_settings if p_settings else CrestSimSettingsClipSurface.new()
	init_sim(p_resolution, p_layers, RenderingDevice.DATA_FORMAT_R8_UNORM, false,
		Color(1, 0, 0, 0) if settings.clip_by_default else Color(0, 0, 0, 0))
	_clear = CrestRDCompute.from_file(rd, "res://addons/crest/shaders/sim/clear_r8.glsl")
	_inject = CrestRDCompute.from_file(rd, "res://addons/crest/shaders/sim/inject_clip.glsl")


## inputs: array of Dictionaries:
##   rect_center/rect_half_size: Vector2
##   mode: 0 = clip, 1 = un-clip
##   texture: Texture2D or null (R channel = coverage mask)
func update_sim(lod_transform: CrestLodTransform, cascade_current: RID, inputs: Array) -> void:
	if _clear == null:
		return
	var u_clear := make_image_uniform(0, false)
	var set_clear := _clear.make_uniform_set([u_clear])
	var clear_value := 1.0 if settings.clip_by_default else 0.0
	_clear.dispatch(resolution / CrestConstants.THREAD_GROUP_SIZE, resolution / CrestConstants.THREAD_GROUP_SIZE, layer_count,
		{0: set_clear}, CrestRDCompute.pack_push_constants(PackedFloat32Array([float(resolution), clear_value])))
	CrestRDCompute.free_rid_deferred(rd, set_clear)

	for input in inputs:
		_dispatch_inject(lod_transform, cascade_current, input)


func _dispatch_inject(lod_transform: CrestLodTransform, cascade_current: RID, input: Dictionary) -> void:
	var u_cascades := RDUniform.new()
	u_cascades.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	u_cascades.binding = 0
	u_cascades.add_id(cascade_current)
	var u_tex := RDUniform.new()
	u_tex.uniform_type = RenderingDevice.UNIFORM_TYPE_SAMPLER_WITH_TEXTURE
	u_tex.binding = 1
	u_tex.add_id(sampler)
	u_tex.add_id(_get_texture_rd(input.get("texture")))
	var u_target := make_image_uniform(2, false)

	var rect_center: Vector2 = input["rect_center"]
	var rect_half: Vector2 = input["rect_half_size"]
	var set := _inject.make_uniform_set([u_cascades, u_tex, u_target])
	var pc := PackedFloat32Array([
		float(resolution), float(layer_count),
		rect_center.x, rect_center.y, rect_half.x, rect_half.y,
		input.get("mode", 0.0), 1.0 if input.get("texture") != null else 0.0,
	])
	_inject.dispatch(resolution / CrestConstants.THREAD_GROUP_SIZE, resolution / CrestConstants.THREAD_GROUP_SIZE, layer_count, {0: set}, CrestRDCompute.pack_push_constants(pc))
	CrestRDCompute.free_rid_deferred(rd, set)


var _tex_cache := {}
var _fallback_tex: RID


func _get_texture_rd(texture: Texture2D) -> RID:
	if texture == null:
		return _get_fallback_tex()
	var key := texture.get_instance_id()
	if _tex_cache.has(key):
		return _tex_cache[key]
	var img := texture.get_image()
	if img == null:
		return _get_fallback_tex()
	img.convert(Image.FORMAT_RGBAF)
	var fmt := RDTextureFormat.new()
	fmt.format = RenderingDevice.DATA_FORMAT_R32G32B32A32_SFLOAT
	fmt.width = img.get_width()
	fmt.height = img.get_height()
	fmt.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT | RenderingDevice.TEXTURE_USAGE_CAN_UPDATE_BIT
	var rid := rd.texture_create(fmt, RDTextureView.new(), [img.get_data()])
	_tex_cache[key] = rid
	return rid


func _get_fallback_tex() -> RID:
	if not _fallback_tex.is_valid():
		var fmt := RDTextureFormat.new()
		fmt.format = RenderingDevice.DATA_FORMAT_R32G32B32A32_SFLOAT
		fmt.width = 1
		fmt.height = 1
		fmt.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT | RenderingDevice.TEXTURE_USAGE_CAN_UPDATE_BIT
		var data := PackedFloat32Array([1.0, 1.0, 1.0, 1.0]).to_byte_array()
		_fallback_tex = rd.texture_create(fmt, RDTextureView.new(), [data])
	return _fallback_tex


func free_rids() -> void:
	if rd:
		if _clear:
			_clear.free_rid()
		if _inject:
			_inject.free_rid()
		for key in _tex_cache:
			rd.free_rid(_tex_cache[key])
		if _fallback_tex.is_valid():
			rd.free_rid(_fallback_tex)
	super.free_rids()
