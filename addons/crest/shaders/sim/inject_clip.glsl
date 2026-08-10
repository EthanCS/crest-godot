// Injects clip surface inputs. Port of Crest's clip surface inputs
// (ClipSurfaceAddFromTex / geometric clip areas): writes 0 (clipped - the
// ocean surface is removed) or 1 (un-clipped) into the R8 cascade.
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
layout(r8, set = 0, binding = 2) uniform image2DArray clip_target;
layout(set = 0, binding = 3) uniform sampler2DArray animated_waves;

layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	vec2 rect_center;
	vec2 rect_half_size;
	float mode; // 0 = clip (write 0), 1 = un-clip (write 1)
	float use_texture;
	float primitive; // -1 = renderer geometry, 0 = sphere, 3 = cube
	float displacement_iterations;
	float ocean_level;
	float _padding;
	vec4 inverse_row_0;
	vec4 inverse_row_1;
	vec4 inverse_row_2;
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

	float coverage = 1.0;
	if (pc.primitive >= -0.5) {
		vec2 undisplaced = world_pos;
		vec3 displacement = vec3(0.0);
		for (int iteration = 0; iteration < int(pc.displacement_iterations); iteration++) {
			vec2 wave_uv = crest_world_to_uv(undisplaced, cascade);
			displacement = textureLod(animated_waves, vec3(wave_uv, float(lod)), 0.0).xyz;
			undisplaced = world_pos - displacement.xz;
		}
		vec4 world = vec4(world_pos.x, pc.ocean_level + displacement.y, world_pos.y, 1.0);
		vec3 local = vec3(dot(pc.inverse_row_0, world), dot(pc.inverse_row_1, world), dot(pc.inverse_row_2, world));
		coverage = pc.primitive < 1.5 ? float(length(local) <= 0.5) :
			float(max(abs(local.x), max(abs(local.y), abs(local.z))) <= 0.5);
	} else if (pc.use_texture > 0.5) {
		coverage = textureLod(input_texture, uv_input, 0.0).x;
	}
	if (coverage < 0.5) {
		return;
	}

	float value = pc.mode < 0.5 ? 0.0 : 1.0;
	imageStore(clip_target, id, vec4(value, 0.0, 0.0, 0.0));
}
