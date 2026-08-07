// Iterative radix-2 IFFT butterfly pass (global memory, one thread per
// butterfly = two outputs). Applied separably: row passes then column
// passes, for all three spectra (height, displaceX, displaceZ) at once.
// The final pass writes the real parts into the RGBA16F wave buffer as
// (dispX, height, dispZ, 0) with 1/N^2 normalisation.
//
// Dispatch: (R/2 / 8, R / 8, 16 cascades).

#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rg32f, set = 0, binding = 0) uniform readonly image2DArray in_h;
layout(rg32f, set = 0, binding = 1) uniform readonly image2DArray in_x;
layout(rg32f, set = 0, binding = 2) uniform readonly image2DArray in_z;

layout(rg32f, set = 0, binding = 3) uniform writeonly image2DArray out_h;
layout(rg32f, set = 0, binding = 4) uniform writeonly image2DArray out_x;
layout(rg32f, set = 0, binding = 5) uniform writeonly image2DArray out_z;

layout(rgba16f, set = 0, binding = 6) uniform writeonly image2DArray wave_buffer;

layout(push_constant, std430) uniform Params {
	float resolution;
	float log2_resolution;
	float pass_index;
	float direction; // 0 = rows (x), 1 = columns (y)
	float final;     // 1 = write to wave buffer with normalisation
}
pc;

const float PI = 3.141592653589793;

// Complex butterfly: (a, b) -> (a + w*b, a - w*b), w = exp(+2*pi*i*k/m)
// (inverse transform convention).
void butterfly(in vec2 a, in vec2 b, float k, float m, out vec2 out0, out vec2 out1) {
	float angle = 2.0 * PI * k / m;
	vec2 w = vec2(cos(angle), sin(angle));
	vec2 wb = vec2(w.x * b.x - w.y * b.y, w.x * b.y + w.y * b.x);
	out0 = a + wb;
	out1 = a - wb;
}

int bit_reverse(int x, int bits) {
	return int(bitfieldReverse(uint(x)) >> uint(32 - bits));
}

void main() {
	ivec3 tid = ivec3(gl_GlobalInvocationID);
	int R = int(pc.resolution);
	if (tid.x >= R / 2 || tid.y >= R) {
		return;
	}
	int layer = tid.z;

	int p = int(pc.pass_index);
	int bits = int(pc.log2_resolution);
	int m = 2 << p;
	int half_m = 1 << p;

	// Thread handles butterfly (group, k) along the transformed axis.
	int group = tid.x / half_m;
	int k = tid.x % half_m;
	int idx0 = group * m + k;
	int idx1 = idx0 + half_m;

	// First pass reads in bit-reversed order.
	int read0 = idx0;
	int read1 = idx1;
	if (p == 0) {
		read0 = bit_reverse(idx0, bits);
		read1 = bit_reverse(idx1, bits);
	}

	ivec3 c0;
	ivec3 c1;
	ivec3 o0;
	ivec3 o1;
	if (pc.direction < 0.5) {
		c0 = ivec3(read0, tid.y, layer);
		c1 = ivec3(read1, tid.y, layer);
		o0 = ivec3(idx0, tid.y, layer);
		o1 = ivec3(idx1, tid.y, layer);
	} else {
		c0 = ivec3(tid.y, read0, layer);
		c1 = ivec3(tid.y, read1, layer);
		o0 = ivec3(tid.y, idx0, layer);
		o1 = ivec3(tid.y, idx1, layer);
	}

	vec2 h0;
	vec2 h1;
	vec2 x0;
	vec2 x1;
	vec2 z0;
	vec2 z1;
	butterfly(imageLoad(in_h, c0).xy, imageLoad(in_h, c1).xy, float(k), float(m), h0, h1);
	butterfly(imageLoad(in_x, c0).xy, imageLoad(in_x, c1).xy, float(k), float(m), x0, x1);
	butterfly(imageLoad(in_z, c0).xy, imageLoad(in_z, c1).xy, float(k), float(m), z0, z1);

	if (pc.final > 0.5) {
		// No 1/N normalisation: the spectrum amplitudes are physical
		// (density * dk^2 already applied), so the plain sum is the
		// displacement (Crest's FFT is unnormalised as well).
		imageStore(wave_buffer, o0, vec4(x0.x, h0.x, z0.x, 0.0));
		imageStore(wave_buffer, o1, vec4(x1.x, h1.x, z1.x, 0.0));
	} else {
		imageStore(out_h, o0, vec4(h0, 0.0, 0.0));
		imageStore(out_h, o1, vec4(h1, 0.0, 0.0));
		imageStore(out_x, o0, vec4(x0, 0.0, 0.0));
		imageStore(out_x, o1, vec4(x1, 0.0, 0.0));
		imageStore(out_z, o0, vec4(z0, 0.0, 0.0));
		imageStore(out_z, o1, vec4(z1, 0.0, 0.0));
	}
}
