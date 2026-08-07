// Crest ocean data access for spatial shaders (surface + underwater).
// Port of Crest's OceanInputsDriven.hlsl / OceanHelpersNew.hlsl.

uniform sampler2DArray ld_animated_waves : filter_linear, repeat_disable;
uniform sampler2DArray ld_foam : filter_linear, repeat_disable;
uniform sampler2DArray ld_sea_floor_depth : filter_linear, repeat_disable;
uniform sampler2DArray ld_flow : filter_linear, repeat_disable;
uniform sampler2DArray ld_shadow : filter_linear, repeat_disable;
uniform sampler2DArray ld_clip_surface : filter_linear, repeat_disable;
uniform sampler2DArray ld_albedo : filter_linear, repeat_disable;

// Per-cascade params (CrestConstants.CASCADE_PARAMS_COUNT = 16):
//   a = (pos_snapped.x, pos_snapped.y, scale, texture_res)
//   b = (one_over_texture_res, texel_width, weight, max_wavelength)
uniform vec4 cascade_data_a[16];
uniform vec4 cascade_data_b[16];
uniform float slice_count;
uniform vec3 ocean_center_pos;
uniform float ocean_scale;
uniform float crest_time;
uniform float lod_alpha_black_point_fade;
uniform float lod_alpha_black_point_white_point_fade;
uniform float mesh_scale_lerp;
uniform float ocean_level;

vec2 crest_world_to_uv(vec2 p, int cascade) {
	vec4 a = cascade_data_a[cascade];
	vec4 b = cascade_data_b[cascade];
	return (p - a.xy) / (b.y * a.w) + 0.5;
}

float crest_cascade_weight(int cascade) {
	return cascade_data_b[cascade].z;
}

float crest_texel_width(int cascade) {
	return cascade_data_b[cascade].y;
}

// World-to-UV that falls back to the next larger cascade when out of
// bounds (Crest: WorldToSafeUV).
vec3 crest_world_to_safe_uv(vec2 p, int cascade) {
	vec2 uv = crest_world_to_uv(p, cascade);
	vec2 d = abs(uv - 0.5);
	if (max(d.x, d.y) > 0.5 - cascade_data_b[cascade].x) {
		cascade = min(cascade + 1, int(slice_count) - 1);
		uv = crest_world_to_uv(p, cascade);
	}
	return vec3(uv, float(cascade));
}

// Samples the animated waves (displacement xyz, variance w).
vec4 crest_sample_displacements(vec3 uv_slice) {
	return textureLod(ld_animated_waves, uv_slice, 0.0);
}

// Rebuilds the surface normal from the displacement texture with a 3-tap
// finite difference, and the 2x2 Jacobian determinant (for SSS/foam).
// Returns the weighted XZ normal contribution (accumulate onto (0,1,0)).
// Port of Crest's SampleDisplacementsNormals.
vec2 crest_sample_normal(vec2 world_pos, int cascade, float weight, inout float jacobian_det) {
	vec3 uv = crest_world_to_safe_uv(world_pos, cascade);
	float texel = crest_texel_width(int(uv.z));
	float one_over_res = cascade_data_b[int(uv.z)].x;
	vec4 disp = crest_sample_displacements(uv);
	vec3 disp_x = vec3(texel, 0.0, 0.0) + crest_sample_displacements(uv + vec3(one_over_res, 0.0, 0.0)).xyz;
	vec3 disp_z = vec3(0.0, 0.0, texel) + crest_sample_displacements(uv + vec3(0.0, one_over_res, 0.0)).xyz;

	vec3 cp = cross(disp_z - disp.xyz, disp_x - disp.xyz);
	cp.y = max(cp.y, 0.0001);
	vec3 n = normalize(cp);

	// Jacobian determinant: < 1 = compression (crest), < 0 = folding.
	mat2 j = mat2(
		(disp_x.xz - disp.xyz.xz) / texel,
		(disp_z.xz - disp.xyz.xz) / texel
	);
	jacobian_det += weight * determinant(j);
	return n.xz * weight;
}

// Samples sea floor depth. Returns water depth (positive = underwater
// terrain distance below sea level) and the sea level offset.
// Port of Crest's SampleSeaDepth.
float crest_sample_sea_depth(vec2 world_pos, int cascade, out float sea_level_offset) {
	vec3 uv = crest_world_to_safe_uv(world_pos, cascade);
	vec2 depth_data = textureLod(ld_sea_floor_depth, uv, 0.0).xy;
	sea_level_offset = depth_data.y;
	return ocean_center_pos.y - depth_data.x + depth_data.y;
}
