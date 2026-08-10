// Crest Dynamic Waves/Add Bump renderer input.
#[compute]
#version 450

#include "include/cascade.glsl"

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) readonly buffer CascadeData { vec4 data[]; } cascades;
layout(rg16f, set = 0, binding = 1) uniform image2DArray dyn_waves;

layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	vec2 center;
	vec2 half_size;
	float amplitude;
	float dt;
	float simulation_count;
} pc;

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	if (id.x >= int(pc.texture_res) || id.y >= int(pc.texture_res)) return;
	CrestCascade cascade;
	CREST_CASCADE_LOAD(cascade, cascades.data, id.z);
	if (min(pc.half_size.x, pc.half_size.y) < cascade.texel_width) return;
	vec2 world = crest_uv_to_world((vec2(id.xy) + 0.5) / pc.texture_res, cascade);
	vec2 offset = (world - pc.center) / max(pc.half_size, vec2(0.0001));
	float r2 = dot(offset, offset);
	if (r2 > 1.0) return;
	r2 = 1.0 - r2;
	float force = pow(r2 * r2, 0.05) * pc.amplitude / max(pc.simulation_count, 1.0);
	vec2 uv = (vec2(id.xy) + 0.5) / pc.texture_res;
	float feather = clamp(min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y)) / 0.1, 0.0, 1.0);
	vec4 previous = imageLoad(dyn_waves, id);
	previous.y += pc.dt * force * feather;
	imageStore(dyn_waves, id, previous);
}
