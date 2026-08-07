class_name CrestRDCompute
extends RefCounted
## Helper that wraps a RenderingDevice compute pipeline compiled from a
## GLSL file (#[compute] / #version 450 style, same as RDShaderFile).

var rd: RenderingDevice
var shader: RID
var pipeline: RID


static func from_file(device: RenderingDevice, glsl_path: String) -> CrestRDCompute:
	# RDShaderFile does not process #include, so resolve includes manually
	# (relative to the including file) and compile from source.
	var source := _resolve_includes(glsl_path, {})
	if source.is_empty():
		push_error("CrestRDCompute: failed to load shader file: " + glsl_path)
		return null
	return from_source(device, source, glsl_path)


static func _resolve_includes(path: String, seen: Dictionary) -> String:
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		return ""
	seen[path] = true
	var out := PackedStringArray()
	var base_dir := path.get_base_dir()
	while not file.eof_reached():
		var line := file.get_line()
		var trimmed := line.strip_edges()
		if trimmed.begins_with('#include'):
			var inc := trimmed.trim_prefix("#include").strip_edges().trim_prefix('"').trim_suffix('"')
			var inc_path := base_dir.path_join(inc)
			if seen.has(inc_path):
				continue
			out.append(_resolve_includes(inc_path, seen))
		else:
			out.append(line)
	return "\n".join(out)


static func from_source(device: RenderingDevice, source: String, debug_name := "") -> CrestRDCompute:
	# shader_compile_spirv_from_source wants pure GLSL: strip the #[compute]
	# stage directive (only RDShaderFile parses it) and make sure #version is
	# the first line.
	var marker := "#[compute]"
	var idx := source.find(marker)
	if idx >= 0:
		source = source.substr(idx + marker.length())
	var lines := source.split("\n", false)
	var version_idx := -1
	for i in lines.size():
		if lines[i].strip_edges().begins_with("#version"):
			version_idx = i
			break
	if version_idx >= 0:
		var version_line: String = lines[version_idx].strip_edges()
		lines.remove_at(version_idx)
		source = version_line + "\n" + "\n".join(lines)

	var src := RDShaderSource.new()
	src.language = RenderingDevice.SHADER_LANGUAGE_GLSL
	src.source_compute = source
	var spirv := device.shader_compile_spirv_from_source(src, false)
	if not spirv.compile_error_compute.is_empty():
		push_error("CrestRDCompute: compile error in %s:\n%s" % [debug_name, spirv.compile_error_compute])
		return null
	return from_spirv(device, spirv, debug_name)


static func from_spirv(device: RenderingDevice, spirv: RDShaderSPIRV, debug_name := "") -> CrestRDCompute:
	if spirv == null or spirv.get_stage_compile_error(RenderingDevice.SHADER_STAGE_COMPUTE) != "":
		push_error("CrestRDCompute: invalid SPIR-V for " + debug_name)
		return null
	var result := CrestRDCompute.new()
	result.rd = device
	result.shader = device.shader_create_from_spirv(spirv)
	if not result.shader.is_valid():
		push_error("CrestRDCompute: shader_create_from_spirv failed for " + debug_name)
		return null
	result.pipeline = device.compute_pipeline_create(result.shader)
	return result


func is_valid() -> bool:
	return pipeline.is_valid()


## Packs push constants: GPU-side blocks are padded to a multiple of
## 16 bytes, so pad the CPU data to match.
static func pack_push_constants(floats: PackedFloat32Array) -> PackedByteArray:
	var bytes := floats.to_byte_array()
	var padded := (bytes.size() + 15) & ~15
	if padded != bytes.size():
		bytes.resize(padded)
	return bytes


# -- Deferred RID freeing -----------------------------------------------------
# Uniform sets bind per-frame ping-ponged textures, so they are recreated
# every dispatch. RenderingDevice.free_rid is immediate, but the GPU may
# still be executing lists that reference the set - free a few frames later.

static var _pending_rids: Array = []
static var _rd: RenderingDevice


static func free_rid_deferred(rd: RenderingDevice, rid: RID) -> void:
	_rd = rd
	_pending_rids.append([rid, 4]) # free after 4 flushes (~frames)


## Call once per frame before dispatching new work.
static func flush_deferred_frees() -> void:
	if _rd == null:
		_pending_rids.clear()
		return
	var keep: Array = []
	for entry in _pending_rids:
		entry[1] -= 1
		if entry[1] <= 0:
			_rd.free_rid(entry[0])
		else:
			keep.append(entry)
	_pending_rids = keep


## Creates a uniform set from an array of RDUniform.
func make_uniform_set(uniforms: Array[RDUniform], set_index := 0) -> RID:
	return rd.uniform_set_create(uniforms, shader, set_index)


## Runs the compute list immediately (not inside an existing compute list).
func dispatch(groups_x: int, groups_y: int, groups_z: int, sets: Dictionary, push_constants := PackedByteArray()) -> void:
	var cl := rd.compute_list_begin()
	rd.compute_list_bind_compute_pipeline(cl, pipeline)
	for set_index in sets:
		rd.compute_list_bind_uniform_set(cl, sets[set_index], set_index)
	if not push_constants.is_empty():
		rd.compute_list_set_push_constant(cl, push_constants, push_constants.size())
	rd.compute_list_dispatch(cl, groups_x, groups_y, groups_z)
	rd.compute_list_end()


func free_rid() -> void:
	if rd and shader.is_valid():
		rd.free_rid(shader)
		shader = RID()
