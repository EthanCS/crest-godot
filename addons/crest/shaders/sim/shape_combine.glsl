// Animated waves combine pass. Port of Crest's ShapeCombine.compute.
//
// Produces the final animated-waves cascade: this LOD's own wave evaluation
// plus every larger cascade's evaluation resampled at this world position
// (independent per layer - mathematically equal to Crest's chain of
// accumulation, without serial dependencies), plus the dynamic-waves sim
// displacement with horizontal sharpening.
//
// One 3D dispatch covers all cascades; gl_GlobalInvocationID.z = LOD index.

#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Per-cascade params, 2 vec4s each (CrestConstants.CASCADE_PARAMS_COUNT = 16):
//   a = (pos_snapped.x, pos_snapped.y, scale, texture_res)
//   b = (one_over_texture_res, texel_width, weight, max_wavelength)
layout(set = 0, binding = 0, std430) readonly buffer CascadeData {
	vec4 data[];
}
cascades;

layout(set = 0, binding = 1) uniform sampler2DArray wave_buffer;
layout(rgba16f, set = 0, binding = 2) uniform writeonly image2DArray animated_waves;
layout(set = 0, binding = 3) uniform sampler2DArray dynamic_waves;

// Convention: all push constants are floats.
layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	// Dynamic waves sharpening (Crest: SimSettingsWave).
	float horiz_displace;
	float displace_clamp;
	float dyn_waves_enabled;
}
pc;

vec2 world_to_uv(vec2 world_pos, int cascade) {
	vec2 snapped = cascades.data[cascade * 2].xy;
	float texel = cascades.data[cascade * 2 + 1].y;
	float res = cascades.data[cascade * 2].w;
	return (world_pos - snapped) / (texel * res) + 0.5;
}

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	if (id.x >= int(pc.texture_res) || id.y >= int(pc.texture_res)) {
		return;
	}
	int lod = id.z;

	vec2 snapped = cascades.data[lod * 2].xy;
	float texel_width = cascades.data[lod * 2 + 1].y;
	vec2 world_pos = snapped + (vec2(id.xy) + 0.5 - pc.texture_res * 0.5) * texel_width;

	// Own evaluation.
	vec4 result = textureLod(wave_buffer, vec3(world_to_uv(world_pos, lod), float(lod)), 0.0);
	float variance = result.w;

	// Accumulate all larger cascades (variance intentionally not merged).
	for (int j = lod + 1; j < int(pc.lod_count); j++) {
		result.xyz += textureLod(wave_buffer, vec3(world_to_uv(world_pos, j), float(j)), 0.0).xyz;
	}

	// Add dynamic waves sim displacement with horizontal sharpening
	// (Crest: ShapeCombine.compute dyn waves section).
	if (pc.dyn_waves_enabled > 0.5) {
		vec2 uv = (vec2(id.xy) + 0.5) / pc.texture_res;
		float e = 1.0 / pc.texture_res;
		float f = textureLod(dynamic_waves, vec3(uv, float(lod)), 0.0).x;
		float fxm = textureLod(dynamic_waves, vec3(uv - vec2(e, 0.0), float(lod)), 0.0).x;
		float fxp = textureLod(dynamic_waves, vec3(uv + vec2(e, 0.0), float(lod)), 0.0).x;
		float fzm = textureLod(dynamic_waves, vec3(uv - vec2(0.0, e), float(lod)), 0.0).x;
		float fzp = textureLod(dynamic_waves, vec3(uv + vec2(0.0, e), float(lod)), 0.0).x;
		result.y += f;

		vec2 grad = vec2(fxp - fxm, fzp - fzm) / (2.0 * texel_width);
		// Wavelength of the sim waves: 2 * texel; wavevector k = 2pi / lambda.
		float wavevector = 6.2831853 / (2.0 * texel_width * 1.5);
		vec2 disp_xz = pc.horiz_displace * grad / wavevector;
		float clamp_abs = texel_width * pc.displace_clamp;
		disp_xz = clamp(disp_xz, vec2(-clamp_abs), vec2(clamp_abs));
		result.xz += disp_xz;
	}

	imageStore(animated_waves, ivec3(id.xy, lod), vec4(result.xyz, variance));
}
