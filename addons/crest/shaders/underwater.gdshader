// Crest underwater post effect. Simplified port of Crest's
// UnderwaterEffect.hlsl full-screen pass (ApplyUnderwaterEffect).
//
// Applied by CrestUnderwaterRenderer through a full-screen ColorRect while
// the camera is below the water surface. Samples the screen and depth
// textures, linearizes depth, then blends the scene towards the water
// scatter colour with a per-channel Beer-Lambert factor. A darkened band
// at the waterline approximates Crest's meniscus.
//
// Scatter parameters mirror ocean.gdshader's underwater branch so the fog
// converges to the same colour the surface backface renders.

shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;
uniform sampler2D depth_texture : hint_depth_texture, repeat_disable, filter_nearest;

// -- Fog (Crest: _DepthFogDensity * DepthFogDensityFactor) ---------------------
uniform vec3 depth_fog_density = vec3(0.9, 0.3, 0.35);

// -- Scattering (Crest: ScatterColour, underwater branch) ----------------------
uniform vec3 diffuse : source_color = vec3(0.0, 0.0027, 0.170);
uniform vec3 diffuse_grazing : source_color = vec3(0.0, 0.0039, 0.169);
uniform vec3 subsurface_colour : source_color = vec3(0.0885, 0.497, 0.456);
uniform float subsurface_base = 0.0;
uniform float subsurface_sun = 1.7;
uniform float subsurface_sun_falloff = 5.0;

// -- Per-frame sync from CrestUnderwaterRenderer --------------------------------
// canvas_item has no INV_PROJECTION_MATRIX / camera built-ins, so the node
// passes them in.
uniform mat4 inv_projection_matrix;
uniform mat3 camera_basis;
uniform vec3 light_dir = vec3(0.0, 1.0, 0.0);
uniform vec3 light_color = vec3(1.0, 1.0, 1.0);
uniform vec3 ambient_light = vec3(0.4, 0.45, 0.5);
// Fade in/out when crossing the surface (0 = pass the scene through).
uniform float effect_strength : hint_range(0.0, 1.0) = 0.0;
// 1 when the camera is right at the surface, 0 beyond the meniscus range.
uniform float meniscus_strength : hint_range(0.0, 1.0) = 0.0;
// Viewport UV y of the waterline (0 = top). Negative disables the band.
uniform float waterline_screen_y = -1.0;


void fragment() {
	vec3 scene = texture(screen_texture, SCREEN_UV).rgb;

	// Linearize depth (same reconstruction as ocean.gdshader).
	float raw_depth = texture(depth_texture, SCREEN_UV).r;
	vec4 upos = inv_projection_matrix * vec4(SCREEN_UV * 2.0 - 1.0, raw_depth, 1.0);
	vec3 pos_vs = upos.xyz / upos.w;
	float scene_z = -pos_vs.z;

	// World-space ray from the camera through this pixel (Crest: -view).
	vec3 ray_ws = camera_basis * normalize(pos_vs);

	// Crest: ScatterColour() underwater branch (shadow = 1, sss = 1, no
	// shallow-water term). Matches ocean.gdshader's surface backface.
	float v = abs(ray_ws.y);
	vec3 scatter_col = mix(diffuse_grazing, diffuse, v) * ambient_light;
	float towards_sun = pow(max(dot(light_dir, ray_ws), 0.0), subsurface_sun_falloff);
	scatter_col += (subsurface_base + subsurface_sun * towards_sun) * subsurface_colour * light_color;

	// Crest: ApplyUnderwaterEffect() - Beer-Lambert absorption per channel,
	// fog distance = linear eye depth of the scene pixel.
	vec3 absorb = clamp(vec3(1.0) - exp(-depth_fog_density * scene_z), vec3(0.0), vec3(1.0));
	vec3 col = mix(scene, scatter_col, absorb);

	// Meniscus: analytic approximation. Crest samples the ocean mask 3 times
	// along the horizon normal and multiplies by 0.9 per mismatching sample;
	// we darken a +-3 px band around the projected waterline by up to 0.85.
	if (meniscus_strength > 0.001 && waterline_screen_y >= 0.0) {
		float dist_px = abs(SCREEN_UV.y - waterline_screen_y) / SCREEN_PIXEL_SIZE.y;
		float band = 1.0 - smoothstep(0.0, 3.0, dist_px);
		col *= 1.0 - 0.15 * band * meniscus_strength;
	}

	COLOR = vec4(mix(scene, col, effect_strength), 1.0);
}
