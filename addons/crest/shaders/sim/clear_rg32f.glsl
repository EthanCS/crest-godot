// Clears every layer of a RG32F texture array to a constant value.

#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rg32f, set = 0, binding = 0) uniform writeonly image2DArray target;

layout(push_constant, std430) uniform Params {
	float texture_res;
	float value_r;
	float value_g;
}
pc;

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	if (id.x >= int(pc.texture_res) || id.y >= int(pc.texture_res)) {
		return;
	}
	imageStore(target, id, vec4(pc.value_r, pc.value_g, 0.0, 0.0));
}
