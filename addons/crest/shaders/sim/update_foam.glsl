// Foam simulation: advected, fading foam injected by wave crests (Jacobian
// determinant) and by shorelines. Port of Crest's UpdateFoam.compute.
//
// Texture format: R16F. One 3D dispatch; gl_GlobalInvocationID.z = LOD index.

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

layout(set = 0, binding = 2) uniform sampler2DArray foam_source;
layout(r16f, set = 0, binding = 3) uniform writeonly image2DArray foam_target;
layout(set = 0, binding = 4) uniform sampler2DArray flow;
layout(set = 0, binding = 5) uniform sampler2DArray animated_waves;
layout(set = 0, binding = 6) uniform sampler2DArray sea_floor_depth;

// Convention: all push constants are floats.
layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	float dt;
	float lod_change;
	float ocean_level;
	float use_source_transforms;
	float foam_fade_rate;
	float wave_foam_strength;
	float wave_foam_coverage;
	float filter_waves; // minimum waves slice foam samples from
	float shoreline_foam_max_depth;
	float shoreline_foam_strength;
	float prewarm; // 1 on first frame / after teleports
}
pc;

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

	float dt = pc.dt;
	// Prewarm: fill the sim as if it had been running.
	float dt_crest = dt;
	float dt_shore = dt;
	if (pc.prewarm > 0.5) {
		dt_crest = max(dt, min(1.0, pc.wave_foam_strength - 1.0) / pc.foam_fade_rate);
		dt_shore = 1.0 / pc.foam_fade_rate;
	}

	// 1. Advection (semi-Lagrangian, by flow).
	vec2 flow_vel = textureLod(flow, vec3(crest_world_to_uv(world_pos, cascade), float(lod)), 0.0).xy;
	vec2 flowed_pos = world_pos - dt * flow_vel;
	vec2 uv_src = crest_world_to_uv(flowed_pos, cascade_src);
	float r_max = 0.5 - 1.0 / pc.texture_res;
	float foam;
	vec2 d = abs(uv_src - 0.5);
	if (max(d.x, d.y) <= r_max) {
		foam = textureLod(foam_source, vec3(uv_src, float(lod_src)), 0.0).x;
	} else if (lod_src + 1 < int(pc.lod_count)) {
		// Fall back to the next larger cascade if it covers the position.
		CrestCascade cascade_next;
		if (pc.use_source_transforms > 0.5) {
			CREST_CASCADE_LOAD(cascade_next, cascades_source.data, lod_src + 1);
		} else {
			CREST_CASCADE_LOAD(cascade_next, cascades_current.data, lod_src + 1);
		}
		vec2 uv_next = crest_world_to_uv(flowed_pos, cascade_next);
		vec2 dn = abs(uv_next - 0.5);
		if (max(dn.x, dn.y) <= r_max) {
			foam = textureLod(foam_source, vec3(uv_next, float(lod_src + 1)), 0.0).x;
		} else {
			foam = 0.0;
		}
	} else {
		foam = 0.0;
	}

	// 2. Dissipation.
	foam *= max(0.0, 1.0 - pc.foam_fade_rate * dt);

	// 3. Wave crest injection via the 2x2 Jacobian determinant.
	int waves_slice = max(int(pc.filter_waves), lod);
	CrestCascade waves_cascade;
	CREST_CASCADE_LOAD(waves_cascade, cascades_current.data, waves_slice);
	vec2 uv_waves = crest_world_to_uv(world_pos, waves_cascade);
	float e = 1.0 / pc.texture_res;
	vec4 data = textureLod(animated_waves, vec3(uv_waves, float(waves_slice)), 0.0);
	vec3 sx = textureLod(animated_waves, vec3(uv_waves + vec2(e, 0.0), float(waves_slice)), 0.0).xyz;
	vec3 sz = textureLod(animated_waves, vec3(uv_waves + vec2(0.0, e), float(waves_slice)), 0.0).xyz;
	float tw = waves_cascade.texel_width;
	vec3 disp_x = vec3(tw, 0.0, 0.0) + sx;
	vec3 disp_z = vec3(0.0, 0.0, tw) + sz;
	// 2x2 Jacobian of the displaced surface.
	mat2 jacobian = mat2(
		(disp_x.xz - data.xz) / tw,
		(disp_z.xz - data.xz) / tw
	);
	float det = determinant(jacobian);
	float foam_base = data.w; // small-wave variance
	foam += 5.0 * dt_crest * pc.wave_foam_strength
		* clamp(pc.wave_foam_coverage - det + foam_base * 0.7, 0.0, 1.0);

	// 4. Shoreline injection (uses the displaced position for depth).
	vec2 uv_disp = crest_world_to_uv(world_pos + data.xz, cascade);
	float terrain_height = textureLod(sea_floor_depth, vec3(uv_disp, float(lod)), 0.0).x;
	float signed_ocean_depth = pc.ocean_level - terrain_height + data.y;
	foam += pc.shoreline_foam_strength * dt_shore
		* clamp(1.0 - signed_ocean_depth / pc.shoreline_foam_max_depth, 0.0, 1.0);

	imageStore(foam_target, id, vec4(clamp(foam, 0.0, 1.0), 0.0, 0.0, 0.0));
}
