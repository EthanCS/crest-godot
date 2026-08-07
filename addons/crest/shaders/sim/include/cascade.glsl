// Shared cascade params helpers for Crest compute shaders.
// Data layout: 2 vec4s per cascade in a storage buffer:
//   a = (pos_snapped.x, pos_snapped.y, scale, texture_res)
//   b = (one_over_texture_res, texel_width, weight, max_wavelength)

struct CrestCascade {
	vec2 pos_snapped;
	float scale;
	float texture_res;
	float one_over_res;
	float texel_width;
	float weight;
	float max_wavelength;
};

// GLSL cannot pass an unsized SSBO array as a function argument, so the
// load is a macro. Usage: CrestCascade c; CREST_CASCADE_LOAD(c, buf.data, i);
#define CREST_CASCADE_LOAD(c, data_arr, i) \
	{ \
		vec4 a_ = data_arr[(i) * 2]; \
		vec4 b_ = data_arr[(i) * 2 + 1]; \
		c.pos_snapped = a_.xy; \
		c.scale = a_.z; \
		c.texture_res = a_.w; \
		c.one_over_res = b_.x; \
		c.texel_width = b_.y; \
		c.weight = b_.z; \
		c.max_wavelength = b_.w; \
	}

vec2 crest_world_to_uv(vec2 p, CrestCascade c) {
	return (p - c.pos_snapped) / (c.texel_width * c.texture_res) + 0.5;
}

vec2 crest_uv_to_world(vec2 uv, CrestCascade c) {
	return c.texel_width * c.texture_res * (uv - 0.5) + c.pos_snapped;
}
