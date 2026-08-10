// Rasterized equivalent of Crest/Inputs/Shadows/Override Shadows.
#[compute]
#version 450

#include "include/cascade.glsl"

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
layout(set = 0, binding = 0, std430) readonly buffer CascadeData { vec4 data[]; } cascades;
layout(set = 0, binding = 1) uniform sampler2D input_texture;
layout(rg8, set = 0, binding = 2) uniform image2DArray shadow_target;

layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	vec2 rect_center;
	vec2 rect_half_size;
	float shadow_value;
	float use_texture;
} pc;

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	if (id.x >= int(pc.texture_res) || id.y >= int(pc.texture_res)) return;
	CrestCascade cascade;
	CREST_CASCADE_LOAD(cascade, cascades.data, id.z);
	vec2 world_pos = crest_uv_to_world((vec2(id.xy) + 0.5) / pc.texture_res, cascade);
	vec2 uv = (world_pos - pc.rect_center) / (2.0 * pc.rect_half_size) + 0.5;
	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return;
	float coverage = pc.use_texture > 0.5 ? textureLod(input_texture, uv, 0.0).r : 1.0;
	if (coverage <= 0.0) return;
	vec4 previous = imageLoad(shadow_target, id);
	previous.rg = mix(previous.rg, vec2(pc.shadow_value), clamp(coverage, 0.0, 1.0));
	imageStore(shadow_target, id, previous);
}
