// FFT spectrum time update. Port of Crest's FFTSpectrum.compute
// (SpectrumUpdate kernel): advances h(k, t) and derives the horizontal
// displacement spectra. Dispatched 3D over (R, R, 16).

#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0) uniform sampler2DArray spectrum_init;
layout(rg32f, set = 0, binding = 1) uniform writeonly image2DArray spectrum_height;
layout(rg32f, set = 0, binding = 2) uniform writeonly image2DArray spectrum_displace_x;
layout(rg32f, set = 0, binding = 3) uniform writeonly image2DArray spectrum_displace_z;

layout(push_constant, std430) uniform Params {
	float resolution;
	float cascade_count;
	float gravity;
	float time;
	float chop;
}
pc;

const float PI = 3.141592653589793;

vec2 cmul(vec2 a, vec2 b) {
	return vec2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	int R = int(pc.resolution);
	if (id.x >= R || id.y >= R) {
		return;
	}

	ivec2 coord = id.xy - R / 2;
	float world_size = 0.5 * float(1 << id.z);
	vec2 k = 2.0 * PI * vec2(coord) / world_size;
	float k_mag = max(length(k), 1e-5);
	float w = sqrt(pc.gravity * k_mag);

	vec4 h0 = texelFetch(spectrum_init, id, 0);
	vec2 fwd = vec2(cos(w * pc.time), -sin(w * pc.time));   // e^{-iwt}
	vec2 bkwd = vec2(cos(w * pc.time), sin(w * pc.time));   // e^{+iwt}
	vec2 h = cmul(h0.xy, fwd) + cmul(h0.zw, bkwd);

	imageStore(spectrum_height, id, vec4(h, 0.0, 0.0));
	// Horizontal displacement spectra: chop * i * (k/|k|) * h.
	imageStore(spectrum_displace_x, id, vec4(pc.chop * vec2(-h.y, h.x) * k.x / k_mag, 0.0, 0.0));
	imageStore(spectrum_displace_z, id, vec4(pc.chop * vec2(-h.y, h.x) * k.y / k_mag, 0.0, 0.0));
}
