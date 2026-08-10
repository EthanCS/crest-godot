// Injects flow inputs into the flow cascade. Ports Crest's
// FlowFixedDirection (replace) and FlowAddFlowMap (additive) input shaders.
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
layout(rg16f, set = 0, binding = 2) uniform image2DArray flow_target;

layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	vec2 rect_center;
	vec2 rect_half_size;
	vec2 fixed_velocity; // for mode 1
	float strength;      // flow map strength (mode 0)
	float mode;          // 0 = add flow map, 1 = fixed direction (replace)
	float use_texture;   // 0 = no texture (uniform), 1 = sample texture
	float flip_x;
	float flip_z;
	float feather_enabled;
	float feather_width;
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

	// Feather at the input rect border (Crest: FeatherWeightFromUV, 0.1).
	float feather = 1.0;
	if (pc.feather_enabled > 0.5) {
		feather = clamp(min(min(uv_input.x, 1.0 - uv_input.x), min(uv_input.y, 1.0 - uv_input.y)) / max(pc.feather_width, 0.001), 0.0, 1.0);
	}

	vec4 prev = imageLoad(flow_target, id);
	vec2 value;
	if (pc.mode < 0.5) {
		// Add flow map: (tex.xy - 0.5) * strength.
		vec2 tex = pc.use_texture > 0.5 ? textureLod(input_texture, uv_input, 0.0).xy : vec2(0.5);
		value = (tex - 0.5) * pc.strength * feather;
		if (pc.flip_x > 0.5) value.x = -value.x;
		if (pc.flip_z > 0.5) value.y = -value.y;
		prev.xy += value;
	} else {
		float coverage = pc.use_texture > 0.5 ? textureLod(input_texture, uv_input, 0.0).r : 1.0;
		value = pc.fixed_velocity;
		prev.xy = mix(prev.xy, value, coverage * feather);
	}
	imageStore(flow_target, id, prev);
}
