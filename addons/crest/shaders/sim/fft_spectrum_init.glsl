// FFT wave spectrum initialisation. Port of Crest's FFTSpectrum.compute
// (SpectrumInitalize kernel). Runs when spectrum/wind settings change.
//
// Writes h0 (complex amplitude pairs) per frequency coordinate:
//   xy = positive direction component (re, im)
//   zw = negative direction component (re, im)
// Dispatched 3D over (R, R, 16 cascades).

#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba32f, set = 0, binding = 0) uniform writeonly image2DArray spectrum_init;

// Per-octave power (linear), 14 entries (disabled octaves = 0).
layout(set = 0, binding = 1, std430) readonly buffer SpectrumControls {
	float powers[];
};

layout(push_constant, std430) uniform Params {
	float resolution;    // R
	float cascade_count; // 16
	float gravity;
	float wind_speed;    // m/s
	vec2 wind_dir;       // unit vector
	float turbulence;
	float seed;
}
pc;

const float PI = 3.141592653589793;
const float SMALLEST_WL_POW_2 = -4.0;

uint wang_hash(uint s) {
	s = (s ^ 61U) ^ (s >> 16U);
	s *= 9U;
	s = s ^ (s >> 4U);
	s *= 0x27d4eb2dU;
	s = s ^ (s >> 15U);
	return s;
}

uint rng_state;

float rand_uniform() {
	// xorshift32
	rng_state ^= (rng_state << 13U);
	rng_state ^= (rng_state >> 17U);
	rng_state ^= (rng_state << 5U);
	return float(rng_state) / float(0xffffffffU);
}

float rand_gauss() {
	// Box-Muller.
	float u1 = max(rand_uniform(), 1e-7);
	float u2 = rand_uniform();
	return sqrt(-2.0 * log(u1)) * cos(2.0 * PI * u2);
}

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	int R = int(pc.resolution);
	if (id.x >= R || id.y >= R) {
		return;
	}

	ivec2 coord = id.xy - R / 2;
	if (coord == ivec2(0)) {
		imageStore(spectrum_init, id, vec4(0.0));
		return;
	}

	// Each cascade keeps only its own frequency band (except the last,
	// which keeps everything); Crest: maxCoord in [4, 8).
	int max_coord = max(abs(coord.x), abs(coord.y));
	if (id.z < int(pc.cascade_count) - 1 && (max_coord < 4 || max_coord >= 8)) {
		imageStore(spectrum_init, id, vec4(0.0));
		return;
	}

	float world_size = 0.5 * float(1 << id.z);
	vec2 k = 2.0 * PI * vec2(coord) / world_size;
	float k_mag = length(k);

	// Dispersion.
	float w = sqrt(pc.gravity * k_mag);
	float dwdk = pc.gravity / (2.0 * w);

	// User spectrum x Pierson-Moskowitz wind term.
	float wavelength = 2.0 * PI / k_mag;
	float octave_index = log2(wavelength) - SMALLEST_WL_POW_2;
	int oi0 = clamp(int(floor(octave_index)), 0, 13);
	int oi1 = clamp(oi0 + 1, 0, 13);
	float alpha = clamp(octave_index - floor(octave_index), 0.0, 1.0);
	float spectrum = mix(powers[oi0], powers[oi1], alpha);
	float wm = 0.87 * pc.gravity / max(pc.wind_speed, 0.01);
	spectrum *= exp(-1.291 * pow(wm / w, 4.0));

	// Directional spreading: cos^2 with turbulence blending to uniform.
	float cos_theta = dot(k, pc.wind_dir) / k_mag;
	const float PI4 = 0.3366; // uniform spreading energy
	float spread_pos = cos_theta > 0.0
		? mix((2.0 / PI) * cos_theta * cos_theta, PI4, pc.turbulence)
		: PI4 * pc.turbulence;
	float spread_neg = -cos_theta > 0.0
		? mix((2.0 / PI) * cos_theta * cos_theta, PI4, pc.turbulence)
		: PI4 * pc.turbulence;

	float dk = 2.0 * PI / world_size;
	float delta_s_pos = spectrum * spread_pos * dk * dk * dwdk / k_mag;
	float delta_s_neg = spectrum * spread_neg * dk * dk * dwdk / k_mag;

	rng_state = wang_hash(uint(id.z * R * R + id.y * R + id.x) + uint(pc.seed * 4096.0));
	float amp_pos = rand_gauss() * sqrt(abs(delta_s_pos) * 2.0);
	float amp_neg = rand_gauss() * sqrt(abs(delta_s_neg) * 2.0);
	float phase_pos = rand_uniform() * 2.0 * PI;
	float phase_neg = rand_uniform() * 2.0 * PI;

	// Crest's visual "spicey" multiplier.
	const float SPICEY = 1.5;
	vec4 out_value = vec4(
		amp_pos * cos(phase_pos), -amp_pos * sin(phase_pos),
		amp_neg * cos(phase_neg), -amp_neg * sin(phase_neg)
	) * SPICEY;
	imageStore(spectrum_init, id, out_value);
}
