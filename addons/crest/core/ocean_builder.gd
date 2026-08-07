class_name CrestOceanBuilder
extends RefCounted
## Generates the ocean surface tiles: concentric LOD rings of grid patches
## around the viewer, with skirts hiding cracks between rings. Port of
## Crest's OceanBuilder.

## Patch types. Skirts are extra rows of verts overlapping neighbour tiles
## (positive) or omitted rows (negative), in vertex units. "Outer" variants
## push their outermost edge out to the horizon.
enum PatchType {
	INTERIOR,
	FAT,
	FAT_X,
	FAT_X_OUTER,
	FAT_XZ,
	FAT_XZ_OUTER,
	FAT_X_SLIM_Z,
	SLIM_X,
	SLIM_XZ,
	SLIM_X_FAT_Z,
	COUNT,
}


## Builds one shared mesh per patch type.
## tile_resolution: verts per tile side minus one (Crest: 0.25*lodRes/geoDownSample).
static func build_patch_meshes(tile_resolution: int, extents_multiplier: float) -> Array:
	var meshes := []
	meshes.resize(PatchType.COUNT)
	for pt in PatchType.COUNT:
		meshes[pt] = _build_ocean_patch(pt, tile_resolution, extents_multiplier)
	return meshes


static func _build_ocean_patch(pt: PatchType, vert_density: float, extents_multiplier: float) -> ArrayMesh:
	var dx := 1.0 / vert_density

	# Skirt widths on left/right/bottom/top (Crest: OceanBuilder.BuildOceanPatch).
	var skirt_x_minus := 0.0
	var skirt_x_plus := 0.0
	var skirt_z_minus := 0.0
	var skirt_z_plus := 0.0
	match pt:
		PatchType.FAT:
			skirt_x_minus = 1.0; skirt_x_plus = 1.0; skirt_z_minus = 1.0; skirt_z_plus = 1.0
		PatchType.FAT_X, PatchType.FAT_X_OUTER:
			skirt_x_plus = 1.0
		PatchType.FAT_XZ, PatchType.FAT_XZ_OUTER:
			skirt_x_plus = 1.0; skirt_z_plus = 1.0
		PatchType.FAT_X_SLIM_Z:
			skirt_x_plus = 1.0; skirt_z_plus = -1.0
		PatchType.SLIM_X:
			skirt_x_plus = -1.0
		PatchType.SLIM_XZ:
			skirt_x_plus = -1.0; skirt_z_plus = -1.0
		PatchType.SLIM_X_FAT_Z:
			skirt_x_plus = -1.0; skirt_z_plus = 1.0

	var side_verts_x := int(1.0 + vert_density + skirt_x_minus + skirt_x_plus)
	var side_verts_z := int(1.0 + vert_density + skirt_z_minus + skirt_z_plus)

	var start_x := -0.5 - skirt_x_minus * dx
	var start_z := -0.5 - skirt_z_minus * dx
	var end_x := 0.5 + skirt_x_plus * dx
	var end_z := 0.5 + skirt_z_plus * dx

	var verts := PackedVector3Array()
	verts.resize(side_verts_x * side_verts_z)
	var vi := 0
	for j in side_verts_z:
		var z := lerpf(start_z, end_z, j / float(side_verts_z - 1))
		# Push outermost edge out to the horizon.
		if pt == PatchType.FAT_XZ_OUTER and j == side_verts_z - 1:
			z *= extents_multiplier
		for i in side_verts_x:
			var x := lerpf(start_x, end_x, i / float(side_verts_x - 1))
			if i == side_verts_x - 1 and (pt == PatchType.FAT_X_OUTER or pt == PatchType.FAT_XZ_OUTER):
				x *= extents_multiplier
			verts[vi] = Vector3(x, 0.0, z)
			vi += 1

	var side_squares_x := side_verts_x - 1
	var side_squares_z := side_verts_z - 1
	var indices := PackedInt32Array()
	for j in side_squares_z:
		for i in side_squares_x:
			var flip_edge := (i % 2 == 1) != (j % 2 == 1)
			var i0: int = i + j * (side_squares_x + 1)
			var i1: int = i0 + 1
			var i2: int = i0 + (side_squares_x + 1)
			var i3: int = i2 + 1
			# Godot front faces are counter-clockwise; Unity's are clockwise,
			# so the winding is reversed relative to Crest's index order.
			if not flip_edge:
				indices.append_array([i0, i1, i3, i3, i2, i0])
			else:
				indices.append_array([i2, i1, i3, i1, i2, i0])

	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)

	# Expand bounds for snapping allowance (Crest: 3 * dx).
	var bounds := mesh.get_aabb()
	bounds = bounds.grow(3.0 * dx)
	mesh.custom_aabb = bounds
	mesh.resource_name = "CrestPatch%d" % pt
	return mesh


## Creates the chunks for one LOD ring under parent. Returns the chunks.
static func create_lod(parent: Node3D, lod_index: int, lod_count: int, meshes: Array, material: Material, extents_multiplier: float) -> Array:
	var horiz_scale := pow(2.0, lod_index)
	var is_biggest_lod := lod_index == lod_count - 1
	var generate_skirt := is_biggest_lod

	var lead_side: PatchType = PatchType.FAT_X_OUTER if generate_skirt else PatchType.SLIM_X
	var trail_side: PatchType = PatchType.FAT_X_OUTER if generate_skirt else PatchType.FAT_X
	var lead_corner: PatchType = PatchType.FAT_XZ_OUTER if generate_skirt else PatchType.SLIM_XZ
	var trail_corner: PatchType = PatchType.FAT_XZ_OUTER if generate_skirt else PatchType.FAT_XZ
	var tl_corner: PatchType = PatchType.FAT_XZ_OUTER if generate_skirt else PatchType.SLIM_X_FAT_Z
	var br_corner: PatchType = PatchType.FAT_XZ_OUTER if generate_skirt else PatchType.FAT_X_SLIM_Z

	var offsets: Array[Vector2] = []
	var patch_types: Array[PatchType] = []

	if lod_index != 0:
		# Outer ring only (hole in the middle filled by the previous LOD).
		offsets = [
			Vector2(-1.5, 1.5), Vector2(-0.5, 1.5), Vector2(0.5, 1.5), Vector2(1.5, 1.5),
			Vector2(-1.5, 0.5), Vector2(1.5, 0.5),
			Vector2(-1.5, -0.5), Vector2(1.5, -0.5),
			Vector2(-1.5, -1.5), Vector2(-0.5, -1.5), Vector2(0.5, -1.5), Vector2(1.5, -1.5),
		]
		patch_types = [
			tl_corner, lead_side, lead_side, lead_corner,
			trail_side, lead_side,
			trail_side, lead_side,
			trail_corner, trail_side, trail_side, br_corner,
		]
	else:
		offsets = [
			Vector2(-1.5, 1.5), Vector2(-0.5, 1.5), Vector2(0.5, 1.5), Vector2(1.5, 1.5),
			Vector2(-1.5, 0.5), Vector2(-0.5, 0.5), Vector2(0.5, 0.5), Vector2(1.5, 0.5),
			Vector2(-1.5, -0.5), Vector2(-0.5, -0.5), Vector2(0.5, -0.5), Vector2(1.5, -0.5),
			Vector2(-1.5, -1.5), Vector2(-0.5, -1.5), Vector2(0.5, -1.5), Vector2(1.5, -1.5),
		]
		patch_types = [
			tl_corner, lead_side, lead_side, lead_corner,
			trail_side, PatchType.INTERIOR, PatchType.INTERIOR, lead_side,
			trail_side, PatchType.INTERIOR, PatchType.INTERIOR, lead_side,
			trail_corner, trail_side, trail_side, br_corner,
		]

	var chunks := []
	for i in offsets.size():
		var pos := offsets[i]
		var pt := patch_types[i]

		var chunk := CrestOceanChunkRenderer.new()
		chunk.name = "Tile_L%d_%d" % [lod_index, pt]
		parent.add_child(chunk)
		chunk.position = horiz_scale * Vector3(pos.x, 0.0, pos.y)
		# Scale only horizontally so the culling box is not stretched in Y.
		chunk.scale = Vector3(horiz_scale, 1.0, horiz_scale)
		chunk.mesh = meshes[pt]
		chunk.material_override = material
		chunk.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		# Innermost 4 tiles draw first, then the rest by LOD index
		# (Crest: OceanBuilder sortingOrder).
		chunk.sorting_offset = -lod_count + (-1.0 if pt == PatchType.INTERIOR else float(lod_index))
		chunk.setup(lod_index, extents_multiplier)

		# Rotate side patches to point the +x side outwards.
		var rotate_x_outwards: bool = pt in [PatchType.FAT_X, PatchType.FAT_X_OUTER, PatchType.SLIM_X, PatchType.SLIM_X_FAT_Z]
		if rotate_x_outwards:
			if absf(pos.y) >= absf(pos.x):
				chunk.rotation_degrees = Vector3(0.0, 90.0 * signf(pos.y) * -1.0, 0.0)
			elif pos.x < 0.0:
				chunk.rotation_degrees = Vector3(0.0, 180.0, 0.0)

		# Rotate corner patches so the +x and +z sides point outwards.
		var rotate_xz_outwards: bool = pt in [PatchType.FAT_XZ, PatchType.SLIM_XZ, PatchType.FAT_X_SLIM_Z, PatchType.FAT_XZ_OUTER]
		if rotate_xz_outwards:
			var from := Vector3(1.0, 0.0, 1.0).normalized()
			var to := chunk.position.normalized()
			if from.dot(to) < -0.99:
				chunk.rotation_degrees = Vector3(0.0, 180.0, 0.0)
			else:
				chunk.quaternion = Quaternion(from, to)

		chunks.append(chunk)
	return chunks


static func get_tile_resolution(lod_data_resolution: int, geo_down_sample_factor: int) -> int:
	return roundi(0.25 * lod_data_resolution / geo_down_sample_factor)
