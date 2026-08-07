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

layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	vec2 center; // world XZ
	float radius;
	float amplitude;
	float blend_mode; // 0 = add, 1 = set height (flatten towards amplitude)
	float feather;    // UV feather width fraction
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

	vec2 rel = world_pos - pc.center;
	float dist2 = dot(rel, rel) / (pc.radius * pc.radius);
	if (dist2 >= 1.0) {
		return;
	}
	// Crest's bump shape: pow((1 - r^2)^2, 0.05) - a flat-topped bump.
	float shape = pow((1.0 - dist2) * (1.0 - dist2), 0.05);

	vec2 uv = (vec2(id.xy) + 0.5) / pc.texture_res;
	float feather_w = clamp(min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y)) / pc.feather, 0.0, 1.0);

	vec4 prev = imageLoad(wave_buffer, id);
	if (pc.blend_mode < 0.5) {
		prev.y += pc.amplitude * shape * feather_w;
	} else {
		// Set height: pull the surface towards the target height.
		prev.y = mix(prev.y, pc.amplitude, shape * feather_w);
	}
	imageStore(wave_buffer, id, prev);
}
