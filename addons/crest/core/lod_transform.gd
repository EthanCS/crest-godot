class_name CrestLodTransform
extends RefCounted
## Tracks the per-cascade transforms (snapped centre position, texel width,
## max wavelength) each frame. Mirrors Crest's LodTransform.
##
## Cascade i covers a square of side 4 * lod_scale(i) centred on the ocean
## root (which follows the viewer), snapped to texel boundaries so the shape
## texels are stationary in world space.

const RENDER_ABOVE_SEA_LEVEL := 10000.0
const RENDER_BELOW_SEA_LEVEL := 10000.0

var lod_count: int
var lod_data_resolution: int

## Per-cascade data, indexed by lod.
var pos_snapped: Array[Vector2] = []
var texel_width := PackedFloat32Array()
var max_wavelength := PackedFloat32Array()

## Packed float data ready for shader upload (current and previous frame).
## Layout per cascade, 2 vec4s:
##   a = (pos_snapped.x, pos_snapped.y, scale, texture_res)
##   b = (one_over_texture_res, texel_width, weight, max_wavelength)
var cascade_data_current := PackedFloat32Array()
var cascade_data_source := PackedFloat32Array()


func _init(p_lod_count: int, p_resolution: int) -> void:
	lod_count = p_lod_count
	lod_data_resolution = p_resolution
	pos_snapped.resize(lod_count)
	texel_width.resize(lod_count)
	max_wavelength.resize(lod_count)
	cascade_data_current.resize(CrestConstants.CASCADE_PARAMS_COUNT * 8)
	cascade_data_source.resize(CrestConstants.CASCADE_PARAMS_COUNT * 8)


func calc_lod_scale(lod_idx: int, ocean_scale: float) -> float:
	return ocean_scale * pow(2.0, lod_idx)


## ocean_scale: current power-of-two scale of the ocean root.
## root_pos: world XZ the cascades centre on (ocean root position).
func update_transforms(ocean_scale: float, root_pos: Vector2) -> void:
	# Flip: current becomes source.
	var tmp := cascade_data_source
	cascade_data_source = cascade_data_current
	cascade_data_current = tmp

	for i in lod_count:
		var lod_scale := calc_lod_scale(i, ocean_scale)
		var cam_ortho_size := 2.0 * lod_scale
		var texel := 2.0 * cam_ortho_size / lod_data_resolution
		texel_width[i] = texel
		# Snap so shape texels are stationary in world space.
		pos_snapped[i] = root_pos - Vector2(fposmod(root_pos.x, texel), fposmod(root_pos.y, texel))
		# 4 texels per wave (2x Nyquist factor). Crest: LodTransform.MaxWavelength.
		max_wavelength[i] = texel * 4.0

	# Write packed cascade params.
	for i in CrestConstants.CASCADE_PARAMS_COUNT:
		var idx := mini(i, lod_count - 1)
		var weight := 1.0
		if i >= lod_count:
			# Duplicate of the last cascade with weight 0 so unconditional
			# slice+1 blending fades out (Crest: LodTransform.WriteCascadeParams).
			weight = 0.0
		var o := i * 8
		cascade_data_current[o + 0] = pos_snapped[idx].x
		cascade_data_current[o + 1] = pos_snapped[idx].y
		cascade_data_current[o + 2] = calc_lod_scale(idx, ocean_scale)
		cascade_data_current[o + 3] = float(lod_data_resolution)
		cascade_data_current[o + 4] = 1.0 / lod_data_resolution
		cascade_data_current[o + 5] = texel_width[idx]
		cascade_data_current[o + 6] = weight
		cascade_data_current[o + 7] = max_wavelength[idx]


## World size of one cascade side.
func cascade_world_size(lod_idx: int, ocean_scale: float) -> float:
	return 4.0 * calc_lod_scale(lod_idx, ocean_scale)


## Rect on XZ covered by a cascade, shrunk by one texel (valid sampling
## region for CPU queries; Crest: LodDataMgrAnimWaves.SuggestDataLOD).
func get_valid_rect(lod_idx: int) -> Rect2:
	var w := texel_width[lod_idx] * lod_data_resolution
	var pos := pos_snapped[lod_idx] - Vector2(w, w) * 0.5
	return Rect2(pos, Vector2(w, w)).grow(-texel_width[lod_idx])


## Picks the smallest cascade whose valid rect contains world_xz.
func suggest_data_lod(world_xz: Vector2, min_lod := 0) -> int:
	for i in range(min_lod, lod_count):
		if get_valid_rect(i).has_point(world_xz):
			return i
	return -1
