// Injects sea floor depth inputs (heightfield texture patches) into the
// depth cascade. Port of Crest's "Cached Depths" input shader
// (BlendOp Max, ColorMask R semantics).
//
// One dispatch per input. In-place read-modify-write.

#[compute]
#version 450

#include "include/cascade.glsl"

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) readonly buffer CascadeData {
	vec4 data[];
}
cascades;

layout(set = 0, binding = 1) uniform sampler2D input_texture;
layout(rg32f, set = 0, binding = 2) uniform image2DArray depth_target;

layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	// Input rect in world XZ: centre and half size.
	vec2 rect_center;
	vec2 rect_half_size;
	float height_offset;
	float sea_level_offset;
	float mode; // 0 = max (terrain), 1 = replace
	float ocean_level;
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
	// Input texture stores object-space height; bring back to world.
	float h = textureLod(input_texture, uv_input, 0.0).x + pc.height_offset;

	vec4 prev = imageLoad(depth_target, id);
	if (pc.mode > 1.5) {
		prev.y = h - pc.ocean_level;
	} else if (pc.mode < 0.5) {
		prev.x = max(prev.x, h);
	} else {
		prev.x = h;
	}
	if (pc.mode < 1.5) prev.y = pc.sea_level_offset;
	imageStore(depth_target, id, prev);
}
