class_name CrestLodDataMgr
extends RefCounted
## Base class for a Crest simulation data manager.
##
## Owns an RD texture with one layer per LOD cascade (mirroring Crest's
## Texture2DArray cascades) and bridges it to the material world through a
## Texture2DArrayRD resource. Sims that integrate state over time (foam,
## dynamic waves, ...) use ping-pong buffering: [method swap_targets] is
## called after the update dispatch.

var rd: RenderingDevice

var resolution := 0
var layer_count := 0
var data_format := RenderingDevice.DATA_FORMAT_R16G16B16A16_SFLOAT

## RD textures; [0] and [1] are the ping-pong pair. Sims without temporal
## state only use [0].
var _textures: Array[RID] = []
var _current := 0

## Resource that can be assigned to shader parameters. Always points at the
## texture that shaders should sample from this frame.
var texture_array := Texture2DArrayRD.new()

## Linear-clamp sampler shared by all sims.
var sampler: RID


func init_sim(p_resolution: int, p_layers: int, p_format: RenderingDevice.DataFormat, p_double_buffered: bool, initial_color := Color(0, 0, 0, 0)) -> void:
	rd = RenderingServer.get_rendering_device()
	resolution = p_resolution
	layer_count = p_layers
	data_format = p_format

	var fmt := RDTextureFormat.new()
	fmt.format = data_format
	fmt.width = resolution
	fmt.height = resolution
	fmt.depth = 1
	fmt.array_layers = layer_count
	fmt.texture_type = RenderingDevice.TEXTURE_TYPE_2D_ARRAY
	fmt.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT \
		| RenderingDevice.TEXTURE_USAGE_STORAGE_BIT \
		| RenderingDevice.TEXTURE_USAGE_CAN_UPDATE_BIT \
		| RenderingDevice.TEXTURE_USAGE_CAN_COPY_FROM_BIT \
		| RenderingDevice.TEXTURE_USAGE_CAN_COPY_TO_BIT

	var count := 2 if p_double_buffered else 1
	var pixel_size := _pixel_size()
	# texture_create wants one data block per array layer.
	var layer_data := PackedByteArray()
	layer_data.resize(pixel_size * resolution * resolution)
	_fill_clear(layer_data, initial_color)
	var layers: Array[PackedByteArray] = []
	for i in layer_count:
		layers.append(layer_data)

	for i in count:
		var tex := rd.texture_create(fmt, RDTextureView.new(), layers)
		if not tex.is_valid():
			push_error("CrestLodDataMgr: texture_create failed (%s)" % get_script().resource_path)
		_textures.append(tex)

	if sampler.is_valid() == false:
		var ss := RDSamplerState.new()
		ss.min_filter = RenderingDevice.SAMPLER_FILTER_LINEAR
		ss.mag_filter = RenderingDevice.SAMPLER_FILTER_LINEAR
		ss.repeat_u = RenderingDevice.SAMPLER_REPEAT_MODE_CLAMP_TO_EDGE
		ss.repeat_v = RenderingDevice.SAMPLER_REPEAT_MODE_CLAMP_TO_EDGE
		sampler = rd.sampler_create(ss)

	texture_array.texture_rd_rid = _textures[_current]


func current_texture() -> RID:
	return _textures[_current]


func target_texture() -> RID:
	return _textures[(_current + 1) % _textures.size()]


## Call after the update pass has written into target_texture().
func swap_targets() -> void:
	if _textures.size() > 1:
		_current = (_current + 1) % _textures.size()
		texture_array.texture_rd_rid = _textures[_current]


## Uniform that binds the sampled (read) texture with the shared sampler.
func make_sampled_uniform(binding: int, use_target := false) -> RDUniform:
	var u := RDUniform.new()
	u.uniform_type = RenderingDevice.UNIFORM_TYPE_SAMPLER_WITH_TEXTURE
	u.binding = binding
	var tex := target_texture() if use_target else current_texture()
	u.add_id(sampler)
	u.add_id(tex)
	return u


## Uniform that binds the writable storage image.
func make_image_uniform(binding: int, use_target := true) -> RDUniform:
	var u := RDUniform.new()
	u.uniform_type = RenderingDevice.UNIFORM_TYPE_IMAGE
	u.binding = binding
	u.add_id(target_texture() if use_target else current_texture())
	return u


func free_rids() -> void:
	# Detach from the material world before freeing so the renderer never
	# touches a stale RID.
	texture_array.texture_rd_rid = RID()
	if rd:
		for tex in _textures:
			if tex.is_valid():
				rd.free_rid(tex)
		if sampler.is_valid():
			rd.free_rid(sampler)
	_textures.clear()
	sampler = RID()


func _pixel_size() -> int:
	match data_format:
		RenderingDevice.DATA_FORMAT_R16G16B16A16_SFLOAT:
			return 8
		RenderingDevice.DATA_FORMAT_R32G32B32A32_SFLOAT:
			return 16
		RenderingDevice.DATA_FORMAT_R32G32_SFLOAT:
			return 8
		RenderingDevice.DATA_FORMAT_R16G16_SFLOAT:
			return 4
		RenderingDevice.DATA_FORMAT_R16_SFLOAT:
			return 2
		RenderingDevice.DATA_FORMAT_R8G8B8A8_UNORM:
			return 4
		RenderingDevice.DATA_FORMAT_R8G8_UNORM:
			return 2
	push_error("CrestLodDataMgr: unhandled format %d" % data_format)
	return 8


func _fill_clear(data: PackedByteArray, color: Color) -> void:
	# Keep it simple: only support zero clear for now, non-zero handled by a
	# compute clear pass if ever needed.
	if color != Color(0, 0, 0, 0):
		var pixel_size := _pixel_size()
		var texel_count := data.size() / pixel_size
		match data_format:
			RenderingDevice.DATA_FORMAT_R16G16B16A16_SFLOAT:
				for i in texel_count:
					data.encode_half(i * pixel_size + 0, color.r)
					data.encode_half(i * pixel_size + 2, color.g)
					data.encode_half(i * pixel_size + 4, color.b)
					data.encode_half(i * pixel_size + 6, color.a)
			RenderingDevice.DATA_FORMAT_R32G32B32A32_SFLOAT:
				for i in texel_count:
					data.encode_float(i * pixel_size + 0, color.r)
					data.encode_float(i * pixel_size + 4, color.g)
					data.encode_float(i * pixel_size + 8, color.b)
					data.encode_float(i * pixel_size + 12, color.a)
			RenderingDevice.DATA_FORMAT_R32G32_SFLOAT:
				for i in texel_count:
					data.encode_float(i * pixel_size + 0, color.r)
					data.encode_float(i * pixel_size + 4, color.g)
			RenderingDevice.DATA_FORMAT_R8G8_UNORM:
				for i in texel_count:
					data.encode_u8(i * pixel_size + 0, int(color.r * 255.0))
					data.encode_u8(i * pixel_size + 1, int(color.g * 255.0))
