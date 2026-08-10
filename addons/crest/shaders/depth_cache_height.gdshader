shader_type spatial;
// Data pass for CrestOceanDepthCache: writes the fragment's world-space Y
// height into the R channel. Rendered by a top-down orthographic camera
// with an untonemapped (linear) environment into the viewport's floating
// point colour buffer; the CPU reads the buffer back into the cache texture.
render_mode unshaded, cull_disabled;

varying float world_y;

uniform float height_min = -1000.0;
uniform float height_range = 1100.0;

vec3 srgb_to_linear(vec3 colour) {
	bvec3 cutoff = lessThanEqual(colour, vec3(0.04045));
	vec3 low = colour / 12.92;
	vec3 high = pow((colour + 0.055) / 1.055, vec3(2.4));
	return mix(high, low, cutoff);
}

void vertex() {
	world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
}

void fragment() {
	float encoded_height = clamp((world_y - height_min) / height_range, 0.0, 1.0);
	encoded_height = min(encoded_height, 0.9999999);
	vec3 packed = fract(encoded_height * vec3(1.0, 255.0, 65025.0));
	packed -= packed.yzz * vec3(1.0 / 255.0, 1.0 / 255.0, 0.0);
	ALBEDO = srgb_to_linear(packed);
}
