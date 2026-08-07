// Injects albedo inputs (overrides the water's scattering colour in an
// area, e.g. algae patches). Port of Crest's albedo input shaders.
// One dispatch per input.

#[compute]
#version 450

#include "include/cascade.glsl"

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) readonly buffer CascadeData {
	vec4 data[];
}
cascades;

layout(set = 0, binding = 1) uniform sampler2D input_texture;
layout(rgba8, set = 0, binding = 2) uniform image2DArray albedo_target;

layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	vec2 rect_center;
	vec2 rect_half_size;
	vec4 tint;
	float use_texture;
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
	vec2 world_pos = crest_uv_to_world((vec2(id.xy) + 0.5) / pc.texture_res, cascade);

	vec2 uv_input = (world_pos - pc.rect_center) / (2.0 * pc.rect_half_size) + 0.5;
	if (uv_input.x < 0.0 || uv_input.x > 1.0 || uv_input.y < 0.0 || uv_input.y > 1.0) {
		return;
	}

	vec4 tex = pc.use_texture > 0.5 ? textureLod(input_texture, uv_input, 0.0) : vec4(1.0);
	vec4 value = tex * pc.tint;
	float feather = clamp(min(min(uv_input.x, 1.0 - uv_input.x), min(uv_input.y, 1.0 - uv_input.y)) / 0.1, 0.0, 1.0);

	vec4 prev = imageLoad(albedo_target, id);
	float a = value.a * feather;
	imageStore(albedo_target, id, vec4(mix(prev.rgb, value.rgb, a), max(prev.a, a)));
}
