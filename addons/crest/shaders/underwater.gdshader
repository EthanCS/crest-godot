// Crest underwater post effect. Simplified port of Crest's
// UnderwaterEffect.hlsl full-screen pass (ApplyUnderwaterEffect).
//
// Applied by CrestUnderwaterRenderer through a full-screen quad (spatial
// shader with clip-space POSITION override; canvas_item shaders cannot
// sample hint_depth_texture) while the camera is below the water surface.
// Samples the screen and depth textures, linearizes depth, then blends the
// scene towards the water scatter colour with a per-channel Beer-Lambert
// factor. A darkened band at the waterline approximates Crest's meniscus.
//
// Scatter parameters mirror ocean.gdshader's underwater branch so the fog
// converges to the same colour the surface backface renders.
//
// Note: like all screen-texture effects in Godot, the screen sample only
// contains the opaque pass - the (transparent) ocean surface itself is not
// included. Underwater that is harmless: those pixels have far/sky depth,
// so the fog saturates and they converge to the scatter colour anyway.

shader_type spatial;
render_mode unshaded, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled;

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
uniform vec3 light_dir = vec3(0.0, 1.0, 0.0);
uniform vec3 light_color = vec3(1.0, 1.0, 1.0);
uniform vec3 ambient_light = vec3(0.4, 0.45, 0.5);
// Fade in/out when crossing the surface (0 = pass the scene through).
uniform float effect_strength : hint_range(0.0, 1.0) = 0.0;
// 1 when the camera is right at the surface, 0 beyond the meniscus range.
uniform float meniscus_strength : hint_range(0.0, 1.0) = 0.0;
// Viewport pixel y of the waterline (0 = top). Negative disables the band.
uniform float waterline_screen_y = -1.0;
// TEMP DEBUG: 1 = visualize scene_z (red) and raw depth (green).
uniform float debug_view = 1.0;


void vertex() {
	// Full-screen triangle/quad in clip space (vertex positions are only
	// used for culling; the node gives the mesh a huge cull margin).
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}


void fragment() {
	vec3 scene = textureLod(screen_texture, SCREEN_UV, 0.0).rgb;

	// Linearize depth (same reconstruction as ocean.gdshader).
	float raw_depth = textureLod(depth_texture, SCREEN_UV, 0.0).r;
	vec4 upos = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw_depth, 1.0);
	vec3 pos_vs = upos.xyz / upos.w;
	float scene_z = -pos_vs.z;

	// TEMP DEBUG: visualize scene_z (10 m = white) and raw depth.
	if (debug_view > 0.5) {
		ALBEDO = vec3(clamp(scene_z / 10.0, 0.0, 1.0), raw_depth, 0.0);
		ALPHA = 1.0;
	} else {

	// World-space ray from the camera through this pixel (Crest: -view).
	vec3 ray_ws = (INV_VIEW_MATRIX * vec4(normalize(pos_vs), 0.0)).xyz;

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
		float dist_px = abs(FRAGCOORD.y - waterline_screen_y);
		float band = 1.0 - smoothstep(0.0, 3.0, dist_px);
		col *= 1.0 - 0.15 * band * meniscus_strength;
	}

	ALBEDO = mix(scene, col, effect_strength);
	// Writing ALPHA forces the transparent pass so the quad draws after the
	// (transparent) ocean surface; render_priority keeps it last.
	ALPHA = 1.0;
	}
}
