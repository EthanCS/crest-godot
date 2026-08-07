// Gerstner wave evaluation. Port of Crest's Gerstner.compute.
// Each wave is a pair of counter-propagating components so that the wave
// field's amplitude varies over time (like an FFT spectrum would produce).
//
// Evaluates all waves belonging to one LOD cascade into the wave buffer
// slice for that cascade. The combine pass afterwards accumulates larger
// wavelengths downwards.

#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

struct Wave {
	// dir.x, dir.z, amplitude, chop_amplitude (already negated)
	vec4 v0;
	// omega, phase, phase2, k (wave number)
	vec4 v1;
};

layout(set = 0, binding = 0, std430) readonly buffer Waves {
	Wave waves[];
};

// readwrite so multiple shape generators can accumulate into the buffer
// (first one per frame passes accumulate = 0, later ones 1).
layout(rgba16f, set = 0, binding = 1) uniform image2DArray wave_buffer;

// Sea floor depth cascade array: x = terrain height (world Y).
layout(set = 0, binding = 2) uniform sampler2DArray sea_floor_depth;

// Convention: all push constants are floats (GDScript PackedFloat32Array);
// integer values are converted with int() on use.
layout(push_constant, std430) uniform Params {
	vec2 pos_snapped;
	float texel_width;
	float texture_res;
	float lod_index;
	float time;
	float reverse_wave_weight;
	float cumulative_variance;
	// Shallows attenuation: strength and median wave k. Disabled when <= 0.
	float attenuation_in_shallows;
	float k_median;
	float ocean_level;
	float wave_start;
	float wave_end;
	// 0 = overwrite (first generator of the frame), 1 = accumulate.
	float accumulate;
}
pc;

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	if (id.x >= int(pc.texture_res) || id.y >= int(pc.texture_res)) {
		return;
	}

	vec2 world_pos = pc.pos_snapped + (vec2(id.xy) + 0.5 - float(pc.texture_res) * 0.5) * pc.texel_width;

	vec3 disp = vec3(0.0);
	for (int i = int(pc.wave_start); i < int(pc.wave_end); i++) {
		Wave w = waves[i];
		float x = w.v1.w * dot(w.v0.xy, world_pos);
		float a1 = x + w.v1.y - w.v1.x * pc.time;
		float a2 = x + w.v1.z + w.v1.x * pc.time;
		float s = sin(a1) + pc.reverse_wave_weight * sin(a2);
		disp.y += w.v0.z * (cos(a1) + pc.reverse_wave_weight * cos(a2));
		disp.x += w.v0.w * s * w.v0.x;
		disp.z += w.v0.w * s * w.v0.y;
	}

	// Attenuate in shallows (Crest: GerstnerShared.hlsl / AnimWavesGerstner).
	float weight = 1.0;
	if (pc.attenuation_in_shallows > 0.0) {
		vec2 depth_uv = (world_pos - pc.pos_snapped) / (pc.texel_width * float(pc.texture_res)) + 0.5;
		float terrain_height = textureLod(sea_floor_depth, vec3(depth_uv, float(pc.lod_index)), 0.0).x;
		float depth = pc.ocean_level - terrain_height;
		weight = pc.attenuation_in_shallows * clamp(depth * pc.k_median / 3.14159265, 0.0, 1.0)
			+ (1.0 - pc.attenuation_in_shallows);
	}
	disp *= weight;

	vec4 out_value = vec4(disp, pc.cumulative_variance);
	if (pc.accumulate > 0.5) {
		vec4 prev = imageLoad(wave_buffer, ivec3(id.xy, int(pc.lod_index)));
		// Displacement accumulates; variance takes the max contribution.
		out_value = vec4(prev.xyz + out_value.xyz, max(prev.w, out_value.w));
	}
	imageStore(wave_buffer, ivec3(id.xy, int(pc.lod_index)), out_value);
}
