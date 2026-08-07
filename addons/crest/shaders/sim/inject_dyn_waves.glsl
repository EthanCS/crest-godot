// Injects sphere water interactions into the dynamic waves sim (velocity
// channel). Port of Crest's DynWavesSphereWaterInteraction.shader - Crest
// rasterises instanced quads with additive blending; here a compute pass
// evaluates the same SDF force per texel. Runs in place (readwrite).

#[compute]
#version 450

#include "include/cascade.glsl"

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) readonly buffer CascadeData {
	vec4 data[];
}
cascades;

struct Sphere {
	// pos.x, pos.z, radius, weight
	vec4 v0;
	// vel.x, vel.y, vel.z, unused
	vec4 v1;
};

layout(set = 0, binding = 1, std430) readonly buffer Spheres {
	Sphere spheres[];
};

layout(rg16f, set = 0, binding = 2) uniform image2DArray dyn_waves;

layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	float sphere_count;
	float dt;
	float weight_up_down_mul;
	float inner_sphere_multiplier;
	float inner_sphere_offset;
}
pc;

// Resolution-independent falloff (band-filtered step, Ottosson).
float interaction_falloff(float a, float x) {
	float ax = a * x;
	float ax2 = ax * ax;
	return ax / (1.0 + ax2 * ax2 * ax2);
}

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	if (id.x >= int(pc.texture_res) || id.y >= int(pc.texture_res)) {
		return;
	}
	int lod = id.z;

	CrestCascade cascade;
	CREST_CASCADE_LOAD(cascade, cascades.data, lod);
	vec2 world_pos = crest_uv_to_world((vec2(id.xy) + 0.5) / pc.texture_res, cascade);
	float min_wavelength = cascade.max_wavelength * 0.5;

	float v_add = 0.0;
	int count = int(pc.sphere_count);
	for (int i = 0; i < count; i++) {
		Sphere s = spheres[i];
		float radius = s.v0.z;
		// Cull: sphere not representable at this cascade resolution.
		if (radius < cascade.texel_width) {
			continue;
		}
		vec2 offset = world_pos - s.v0.xy;
		float dist = length(offset);
		float signed_dist = dist - radius;
		vec2 sdf_normal = offset / max(dist, 1e-4);
		vec3 vel = s.v1.xyz;
		vel.y *= pc.weight_up_down_mul;

		// Vertical motion: push in the same direction inside the sphere,
		// opposite outside.
		float force_up_down = -vel.y * interaction_falloff(1.67 / min_wavelength, signed_dist);

		// Horizontal motion: raise in the direction of motion, drop behind.
		float force_horiz = 0.0;
		if (signed_dist > 0.0 || signed_dist < -radius * pc.inner_sphere_offset) {
			force_horiz = sign(signed_dist) * dot(sdf_normal, vel.xz)
				* interaction_falloff(1.43 / min_wavelength, abs(signed_dist));
			if (signed_dist < 0.0) {
				force_horiz *= pc.inner_sphere_multiplier;
			}
		}

		v_add += s.v0.w * (force_up_down + force_horiz) * 0.2 / min_wavelength;
	}

	if (abs(v_add) > 1e-6) {
		vec2 uv = (vec2(id.xy) + 0.5) / pc.texture_res;
		// Feather at cascade edges.
		float feather = clamp(min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y)) / 0.1, 0.0, 1.0);
		vec4 prev = imageLoad(dyn_waves, id);
		prev.y += v_add * feather * pc.dt;
		imageStore(dyn_waves, id, prev);
	}
}
