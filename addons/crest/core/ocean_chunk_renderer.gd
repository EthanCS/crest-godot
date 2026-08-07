@tool
class_name CrestOceanChunkRenderer
extends MeshInstance3D
## One ocean tile. Receives its cascade (slice) index as a per-instance
## shader parameter and expands its culling bounds for wave displacement.
## Port of Crest's OceanChunkRenderer.

var _lod_index := 0


func setup(lod_index: int, _extents_multiplier: float) -> void:
	_lod_index = lod_index
	set_instance_shader_parameter(&"ld_slice_index", float(lod_index))


## Expands the culling bounds so displaced vertices stay inside them
## (Crest: OceanChunkRenderer.ExpandBoundsForDisplacements).
func expand_bounds(max_horiz_displacement: float, max_vert_displacement: float) -> void:
	if mesh == null:
		return
	var bounds := mesh.get_aabb()
	var horiz := max_horiz_displacement / maxf(scale.x, 0.001)
	bounds.position.x -= horiz
	bounds.position.z -= horiz
	bounds.size.x += 2.0 * horiz
	bounds.size.z += 2.0 * horiz
	# Y is not scaled on the chunk transform (see OceanBuilder).
	bounds.position.y -= max_vert_displacement
	bounds.size.y += 2.0 * max_vert_displacement
	custom_aabb = bounds
