// Shadow sim: soft shadows on the water surface. Port of Crest's
// UpdateShadow.hlsl jitter + exponential-moving-average accumulation.
//
// Crest samples the engine's shadow map; that is not accessible from
// Godot's RenderingDevice, so occlusion is evaluated analytically against
// registered sphere casters (CrestSphereWaterInteraction-style inputs).
// The jitter + EMA accumulation produces the same soft-edge behaviour.
//
// Format: RG8, R = soft shadow, G = hard shadow (1 = lit).

#[compute]
#version 450

#include "include/cascade.glsl"

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) readonly buffer CascadeDataCurrent {
	vec4 data[];
}
cascades_current;
layout(set = 0, binding = 1, std430) readonly buffer CascadeDataSource {
	vec4 data[];
}
cascades_source;

layout(set = 0, binding = 2) uniform sampler2DArray shadow_source;
layout(rg8, set = 0, binding = 3) uniform image2DArray shadow_target;

struct Caster {
	// pos.xyz, radius
	vec4 v0;
};

layout(set = 0, binding = 4, std430) readonly buffer Casters {
	Caster casters[];
};

layout(set = 0, binding = 5) uniform sampler2DArray animated_waves;

layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	float dt;
	float lod_change;
	float caster_count;
	float jitter_diameter_soft;
	float weight_soft;
	float jitter_diameter_hard;
	float weight_hard;
	vec3 light_dir; // towards the light
	float time;
	float ocean_level;
	float use_source_transforms;
}
pc;

uint crest_base_hash(uvec3 p) {
	p = 1103515245U * ((p.xyz >> 1U) ^ (p.yzx));
	uint h32 = 1103515245U * ((p.x ^ p.z) ^ (p.y >> 3U));
	return h32 ^ (h32 >> 16);
}

vec3 crest_hash33(uvec3 x) {
	uint n = crest_base_hash(x);
	uvec3 rz = uvec3(n, n * 16807U, n * 48271U);
	return vec3(rz & uvec3(0x7fffffffU)) / float(0x7fffffff);
}

// 1 = lit, 0 = fully shadowed.
float compute_occlusion(vec3 p) {
	float lit = 1.0;
	int count = int(pc.caster_count);
	for (int i = 0; i < count; i++) {
		vec3 c = casters[i].v0.xyz;
		float r = casters[i].v0.w;
		// Ray from p towards the light vs sphere.
		vec3 oc = p - c;
		float b = dot(oc, pc.light_dir);
		float cc = dot(oc, oc) - r * r;
		float disc = b * b - cc;
		if (disc > 0.0) {
			float t = -b + sqrt(disc);
			if (t > 0.0) {
				lit = 0.0;
				break;
			}
		}
	}
	return lit;
}

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	if (id.x >= int(pc.texture_res) || id.y >= int(pc.texture_res)) {
		return;
	}
	int lod = id.z;

	CrestCascade cascade;
	CREST_CASCADE_LOAD(cascade, cascades_current.data, lod);
	vec2 world_pos = crest_uv_to_world((vec2(id.xy) + 0.5) / pc.texture_res, cascade);

	int lod_src = clamp(lod + int(pc.lod_change), 0, int(pc.lod_count) - 1);
	CrestCascade cascade_src;
	if (pc.use_source_transforms > 0.5) {
		CREST_CASCADE_LOAD(cascade_src, cascades_source.data, lod_src);
	} else {
		CREST_CASCADE_LOAD(cascade_src, cascades_current.data, lod_src);
	}

	vec2 uv_src = crest_world_to_uv(world_pos, cascade_src);
	vec2 prev_shadow = vec2(1.0);
	vec2 d = abs(uv_src - 0.5);
	if (max(d.x, d.y) <= 0.5 - 1.0 / pc.texture_res) {
		prev_shadow = textureLod(shadow_source, vec3(uv_src, float(lod_src)), 0.0).xy;
	}

	// Displace the sample position with the waves so shadows stick to the
	// surface (Crest: positionWS += displacement).
	vec3 disp = textureLod(animated_waves, vec3(crest_world_to_uv(world_pos, cascade), float(lod)), 0.0).xyz;
	vec3 p = vec3(world_pos.x, pc.ocean_level, world_pos.y) + disp;

	vec3 jitter_seed = crest_hash33(uvec3(uvec2(abs(world_pos * 10.0)), uint(pc.time * 120.0)));

	vec3 p_soft = p + (jitter_seed - 0.5) * pc.jitter_diameter_soft;
	float soft = compute_occlusion(p_soft);
	soft = mix(prev_shadow.x, soft, clamp(pc.weight_soft * pc.dt * 60.0, 0.0, 1.0));

	vec3 p_hard = p + (crest_hash33(uvec3(uvec2(abs(world_pos * 10.0)), uint(pc.time * 120.0) + 7U)) - 0.5) * pc.jitter_diameter_hard;
	float hard = compute_occlusion(p_hard);
	hard = mix(prev_shadow.y, hard, clamp(pc.weight_hard * pc.dt * 60.0, 0.0, 1.0));

	imageStore(shadow_target, id, vec4(soft, hard, 0.0, 1.0));
}
