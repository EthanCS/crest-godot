// Dynamic waves simulation: a damped 2D wave equation with semi-Lagrangian
// advection, simulating interactive ripples/wakes. Port of Crest's
// UpdateDynWaves.compute.
//
// Texture format: RG16F, x = displacement (height), y = velocity.
// One 3D dispatch covers all cascades; gl_GlobalInvocationID.z = LOD index.

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

layout(set = 0, binding = 2) uniform sampler2DArray dyn_waves_source;
layout(rg16f, set = 0, binding = 3) uniform writeonly image2DArray dyn_waves_target;
layout(set = 0, binding = 4) uniform sampler2DArray flow;
layout(set = 0, binding = 5) uniform sampler2DArray sea_floor_depth;

// Convention: all push constants are floats.
layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	float dt; // 0 means advection-only substep
	float gravity;
	float damping;
	float courant_number;
	float attenuation_in_shallows;
	float lod_change;
	float ocean_level;
	float use_source_transforms; // 1 on the first substep of a frame
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

	float grid_size = cascade.texel_width;
	float wavelength = 2.0 * grid_size;
	float dt_safe = max(pc.dt, 1e-5);
	float c = sqrt(wavelength * pc.gravity / 6.2831853);
	c = min(c, pc.courant_number * grid_size / dt_safe);

	// Shallow water: reflect at the shoreline.
	float terrain_height = textureLod(sea_floor_depth,
		vec3(crest_world_to_uv(world_pos, cascade), float(lod)), 0.0).x;
	float water_depth = pc.ocean_level - terrain_height;
	if (water_depth <= 0.0) {
		imageStore(dyn_waves_target, id, vec4(0.0));
		return;
	}

	// Source cascade (previous frame when use_source_transforms, with LOD
	// change migration; Crest: sliceIndexSource = id.z + _LODChange).
	int lod_src = lod + int(pc.lod_change);
	if (lod_src < 0 || lod_src >= int(pc.lod_count)) {
		imageStore(dyn_waves_target, id, vec4(0.0));
		return;
	}
	CrestCascade cascade_src;
	if (pc.use_source_transforms > 0.5) {
		CREST_CASCADE_LOAD(cascade_src, cascades_source.data, lod_src);
	} else {
		CREST_CASCADE_LOAD(cascade_src, cascades_current.data, lod_src);
	}

	// Semi-Lagrangian advection by flow.
	vec2 flow_vel = textureLod(flow, vec3(crest_world_to_uv(world_pos, cascade), float(lod)), 0.0).xy;
	vec2 uv_src = crest_world_to_uv(world_pos - pc.dt * flow_vel, cascade_src);

	// Edge weight to suppress streaks when the source position leaves the
	// previously covered region. Also gate samples clearly OUTSIDE the
	// source window to zero: after a big camera jump (fast flight, or a
	// pause-move-resume) the clamped edge content would otherwise smear
	// across the newly exposed band and corrupt the whole wake pattern.
	float dist_to_edge = min(min(uv_src.x, 1.0 - uv_src.x), min(uv_src.y, 1.0 - uv_src.y));
	float weight_edge = mix(0.95, 1.0, clamp(dist_to_edge / 0.1, 0.0, 1.0));
	weight_edge *= smoothstep(-0.03, -0.005, dist_to_edge);

	float e = 1.0 / pc.texture_res;
	vec2 fv = textureLod(dyn_waves_source, vec3(uv_src, float(lod_src)), 0.0).xy;
	float f = fv.x;
	float v = fv.y;
	float fxm = textureLod(dyn_waves_source, vec3(uv_src - vec2(e, 0.0), float(lod_src)), 0.0).x;
	float fxp = textureLod(dyn_waves_source, vec3(uv_src + vec2(e, 0.0), float(lod_src)), 0.0).x;
	float fzm = textureLod(dyn_waves_source, vec3(uv_src - vec2(0.0, e), float(lod_src)), 0.0).x;
	float fzp = textureLod(dyn_waves_source, vec3(uv_src + vec2(0.0, e), float(lod_src)), 0.0).x;

	// Wave equation (explicit Euler).
	float laplacian = fxm + fxp + fzm + fzp - 4.0 * f;
	v += pc.dt * c * c / (grid_size * grid_size) * laplacian;
	v *= 1.0 - min(1.0, pc.damping * pc.dt);
	v *= weight_edge;
	f = (f + pc.dt * v) * weight_edge;

	// Attenuate in shallows (wave breaking).
	float depth_mul = 1.0 - (1.0 - clamp(2.0 * water_depth / wavelength, 0.0, 1.0)) * pc.dt * 2.0;
	f *= mix(1.0, depth_mul, pc.attenuation_in_shallows);

	// A trough cannot push the surface below the sea floor (it would expose
	// the seabed through the surface). Clamp to a fraction of the water
	// column; a no-op in deep water.
	f = max(f, -0.9 * water_depth);

	if (isnan(f) || isinf(f) || isnan(v) || isinf(v)) {
		f = 0.0;
		v = 0.0;
	}

	imageStore(dyn_waves_target, id, vec4(f, v, 0.0, 0.0));
}
