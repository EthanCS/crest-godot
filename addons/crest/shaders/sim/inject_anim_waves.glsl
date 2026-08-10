// Injects local displacement bumps into the wave buffer (before the
// combine pass). Port of Crest's DynWavesAddBump-style inputs applied to
// the animated waves. One dispatch per input.

#[compute]
#version 450

#include "include/cascade.glsl"

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) readonly buffer CascadeData {
	vec4 data[];
}
cascades;

layout(rgba16f, set = 0, binding = 1) uniform image2DArray wave_buffer;
layout(set = 0, binding = 2) uniform sampler2D input_texture;

layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	vec2 center; // world XZ
	vec2 rect_half_size;
	float amplitude;
	float blend_mode; // 0 = add texture, 1 = set geometry height
	float heights_only;
	float ocean_level;
	float wavelength; // -1 = all pre-combine, 0 = all post-combine, >0 = octave
}
pc;

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	if (id.x >= int(pc.texture_res) || id.y >= int(pc.texture_res)) {
		return;
	}
	int lod = id.z;

	CrestCascade cascade;
	CREST_CASCADE_LOAD(cascade, cascades.data, lod);
	if (pc.wavelength > 0.0 &&
		(pc.wavelength < cascade.max_wavelength * 0.5 || pc.wavelength >= cascade.max_wavelength)) return;
	vec2 world_pos = crest_uv_to_world((vec2(id.xy) + 0.5) / pc.texture_res, cascade);

	vec2 uv_input = (world_pos - pc.center) / (2.0 * pc.rect_half_size) + 0.5;
	if (uv_input.x < 0.0 || uv_input.x > 1.0 || uv_input.y < 0.0 || uv_input.y > 1.0) {
		return;
	}
	vec4 input_value = textureLod(input_texture, uv_input, 0.0);
	if (input_value.a <= 0.0) return;

	vec4 prev = imageLoad(wave_buffer, id);
	if (pc.blend_mode < 0.5) {
		if (pc.heights_only > 0.5) prev.y += input_value.r * pc.amplitude;
		else prev.xyz += input_value.rgb * pc.amplitude;
	} else {
		prev.y = mix(prev.y, input_value.r - pc.ocean_level, input_value.a);
	}
	imageStore(wave_buffer, id, prev);
}
