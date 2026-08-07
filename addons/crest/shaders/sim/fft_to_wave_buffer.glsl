// Samples the tiled FFT displacement output into the view-following wave
// buffer cascades (Crest: AnimWavesGerstner.shader role). One 3D dispatch;
// gl_GlobalInvocationID.z = LOD index. Accumulates when requested so
// multiple shape generators can coexist.

#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// FFT output tiles with repeat wrapping (world_size = 0.5 * 2^fft_slice).
layout(set = 0, binding = 0) uniform sampler2DArray fft_wave_buffer;

layout(rgba16f, set = 0, binding = 1) uniform image2DArray wave_buffer;

// Per-LOD fft slice indices (which FFT cascade tiles each LOD samples).
layout(set = 0, binding = 2, std430) readonly buffer SliceMap {
	int slices[];
};

// Per-cascade params of the view-following cascades (2 vec4s each).
layout(set = 0, binding = 3, std430) readonly buffer CascadeData {
	vec4 data[];
}
cascades;

layout(push_constant, std430) uniform Params {
	float texture_res;
	float lod_count;
	float accumulate;
	float weight;
	float variance;
}
pc;

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	if (id.x >= int(pc.texture_res) || id.y >= int(pc.texture_res)) {
		return;
	}
	int lod = id.z;

	vec4 a = cascades.data[lod * 2];
	vec4 b = cascades.data[lod * 2 + 1];
	vec2 world_pos = a.xy + (vec2(id.xy) + 0.5 - pc.texture_res * 0.5) * b.y;

	int fft_slice = slices[lod];
	float world_size = 0.5 * float(1 << fft_slice);
	vec2 uv = world_pos / world_size;

	vec3 disp = textureLod(fft_wave_buffer, vec3(uv, float(fft_slice)), 0.0).xyz * pc.weight;

	vec4 prev = vec4(0.0);
	if (pc.accumulate > 0.5) {
		prev = imageLoad(wave_buffer, id);
	}
	imageStore(wave_buffer, id, vec4(prev.xyz + disp, max(prev.w, pc.variance)));
}
