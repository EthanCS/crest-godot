// GPU noise helpers ported from Crest's GPUNoise.hlsl (MIT, by nimitz / stormoid).
// See addons/crest/textures and _reference SOURCE.txt for attribution.

uint crest_base_hash(uvec3 p) {
	p = 1103515245U * ((p.xyz >> 1U) ^ (p.yzx));
	uint h32 = 1103515245U * ((p.x ^ p.z) ^ (p.y >> 3U));
	return h32 ^ (h32 >> 16);
}

float crest_hash13(uvec3 x) {
	uint n = crest_base_hash(x);
	return float(n) * (1.0 / float(0xffffffffU));
}

vec2 crest_hash23(uvec3 x) {
	uint n = crest_base_hash(x);
	uvec2 rz = uvec2(n, n * 48271U);
	return vec2(rz.xy & uvec2(0x7fffffffU)) / float(0x7fffffff);
}

vec3 crest_hash33(uvec3 x) {
	uint n = crest_base_hash(x);
	uvec3 rz = uvec3(n, n * 16807U, n * 48271U);
	return vec3(rz & uvec3(0x7fffffffU)) / float(0x7fffffff);
}
