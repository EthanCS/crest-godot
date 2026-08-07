shader_type spatial;
// Data pass for CrestOceanDepthCache: writes the fragment's world-space Y
// height into the R channel. Rendered by a top-down orthographic camera
// with an untonemapped (linear) environment into the viewport's floating
// point colour buffer; the CPU reads the buffer back into the cache texture.
render_mode unshaded, cull_disabled;

varying float world_y;

void vertex() {
	world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
}

void fragment() {
	ALBEDO = vec3(world_y, 0.0, 0.0);
}
