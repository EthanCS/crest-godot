// Crest ocean surface shader. Port of Crest's Ocean.shader (BIRP variant).
//
// Vertex: grid snapping, LOD morphing, dual-cascade displacement blending.
// Fragment: normals from displacement, detail normal maps, foam, SSS,
// sky/planar reflections with fresnel, refraction with depth absorption,
// caustics, shoreline feathering, clip surface, underwater backface.

shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;

#include "res://addons/crest/shaders/include/crest_ocean_data.gdshaderinc"

// -- Geometry ---------------------------------------------------------------
uniform float base_mesh_density = 48.0;

// -- Lighting (synced from the scene by CrestOceanRenderer) ------------------
uniform vec3 light_dir = vec3(0.0, 1.0, 0.0);
uniform vec3 light_color = vec3(1.0, 1.0, 1.0);
uniform vec3 ambient_light = vec3(0.4, 0.45, 0.5);

// -- Normals -----------------------------------------------------------------
uniform float normals_strength_overall = 1.0;
uniform float normals_scale = 40.0;
uniform float normals_strength = 0.36;
uniform sampler2D normals_texture : filter_linear_mipmap, repeat_enable;

// -- Scattering --------------------------------------------------------------
uniform vec3 diffuse = vec3(0.0, 0.0027, 0.170);
uniform vec3 diffuse_grazing = vec3(0.0, 0.0039, 0.169);
uniform vec3 diffuse_shadow = vec3(0.0, 0.0013, 0.085);
uniform vec3 subsurface_shallow_col = vec3(0.0, 0.0039, 0.247);
uniform float subsurface_depth_max = 10.0;
uniform float subsurface_depth_power = 2.5;
uniform vec3 subsurface_colour = vec3(0.0885, 0.497, 0.456);
uniform float subsurface_base = 0.0;
uniform float subsurface_sun = 1.7;
uniform float subsurface_sun_falloff = 5.0;

// -- Reflection ----------------------------------------------------------------
uniform float specular = 0.7;
uniform float fresnel_power = 5.0;
uniform float refractive_index_of_water = 1.333;
uniform float directional_light_boost = 7.0;
uniform float directional_light_falloff = 275.0;
uniform vec3 sky_base = vec3(0.35, 0.54, 0.75);
uniform vec3 sky_towards_sun = vec3(0.75, 0.78, 0.8);
uniform vec3 sky_away_from_sun = vec3(0.28, 0.38, 0.55);
uniform float sky_directionality = 0.9;
uniform sampler2D planar_reflection : filter_linear, repeat_disable;
uniform float planar_reflection_intensity = 0.0;
uniform float planar_reflection_normals_strength = 1.0;

// -- Foam ---------------------------------------------------------------------
uniform sampler2D foam_texture : filter_linear_mipmap, repeat_enable;
uniform float foam_scale = 10.0;
uniform float wave_foam_feather = 0.4;
uniform float wave_foam_light_scale = 1.35;
uniform float shoreline_foam_min_depth = 0.27;
uniform float wave_foam_normal_strength = 3.5;
uniform float wave_foam_specular_falloff = 293.0;
uniform float wave_foam_specular_boost = 0.15;
uniform vec3 foam_white_color = vec3(1.0, 1.0, 1.0);
uniform float foam_white_alpha = 1.0;
uniform vec3 foam_bubble_color = vec3(0.64, 0.83, 0.82);
uniform float foam_bubble_parallax = 0.14;
uniform float wave_foam_bubbles_coverage = 1.68;

// -- Transparency / refraction -------------------------------------------------
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D depth_texture : hint_depth_texture, filter_linear;
uniform vec3 depth_fog_density = vec3(0.9, 0.3, 0.35);
uniform float refraction_strength = 0.5;

// -- Caustics ------------------------------------------------------------------
uniform sampler2D caustics_texture : filter_linear_mipmap, repeat_enable;
uniform float caustics_strength = 3.2;
uniform float caustics_texture_scale = 5.0;
uniform float caustics_texture_average = 0.07;
uniform float caustics_focal_depth = 2.0;
uniform float caustics_depth_of_field = 0.33;
uniform float caustics_distortion_strength = 0.16;
uniform float caustics_distortion_scale = 25.0;

// -- Sim toggles (0/1), synced from the ocean renderer ---------------------------
uniform float enable_foam = 1.0;
uniform float enable_shadow = 0.0;
uniform float enable_clip_surface = 0.0;
uniform float enable_sea_floor_depth = 1.0;
uniform float enable_albedo = 0.0;
uniform float enable_caustics = 1.0;

instance uniform float ld_slice_index;

varying vec3 v_world_pos;
varying vec2 v_undisplaced_xz;
varying float v_lod_alpha;
varying float v_slice;
varying float v_variance;
varying float v_sea_level_offset;


void vertex() {
	vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec2 tile_origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xz;
	int slice = int(ld_slice_index);

	float geo_grid_width = cascade_data_a[slice].z / base_mesh_density;

	// 1. Snap the tile to twice the grid size (prevents crawling).
	float grid2 = 2.0 * geo_grid_width;
	world_pos.xz -= fract(tile_origin / grid2) * grid2;

	// 2. LOD alpha: 0 at one tile width from centre, 1 at two.
	vec2 d = abs(world_pos.xz - ocean_center_pos.xz);
	float taxicab = max(d.x, d.y);
	float lod_alpha = taxicab / cascade_data_a[0].z - 1.0;
	lod_alpha = max((lod_alpha - lod_alpha_black_point_fade) / lod_alpha_black_point_white_point_fade, 0.0);
	// Clamp: outer rings would otherwise morph vertices unboundedly.
	lod_alpha = min(lod_alpha, 1.0);
	if (slice == 0) {
		lod_alpha = min(lod_alpha + mesh_scale_lerp, 1.0);
	}

	// 3. Morph vertices towards the next-coarser LOD grid (crack-free).
	float grid4 = 4.0 * geo_grid_width;
	vec2 m = fract(world_pos.xz / grid4);
	vec2 off = m - 0.5;
	if (abs(off.x) < 0.375) {
		world_pos.x += off.x * lod_alpha * grid4;
	}
	if (abs(off.y) < 0.375) {
		world_pos.z += off.y * lod_alpha * grid4;
	}

	// 4. Displacement from the animated waves cascades (dual blend).
	int c0 = slice;
	int c1 = slice + 1;
	float wt0 = (1.0 - lod_alpha) * crest_cascade_weight(c0);
	float wt1 = (1.0 - wt0) * crest_cascade_weight(c1);
	v_undisplaced_xz = world_pos.xz;
	float variance = 0.0;
	if (wt0 > 0.001) {
		vec4 s = crest_sample_displacements(vec3(crest_world_to_uv(v_undisplaced_xz, c0), float(c0)));
		world_pos += wt0 * s.xyz;
		variance += wt0 * s.w;
	}
	if (wt1 > 0.001) {
		vec4 s = crest_sample_displacements(vec3(crest_world_to_uv(v_undisplaced_xz, c1), float(c1)));
		world_pos += wt1 * s.xyz;
		variance += wt1 * s.w;
	}

	// 5. Sea level offset (varying water level bodies).
	float sea_level_offset = 0.0;
	if (enable_sea_floor_depth > 0.5) {
		crest_sample_sea_depth(world_pos.xz, c0, sea_level_offset);
		world_pos.y += sea_level_offset;
	}

	v_world_pos = world_pos;
	v_lod_alpha = lod_alpha;
	v_slice = float(slice);
	v_variance = variance;
	v_sea_level_offset = sea_level_offset;

	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world_pos, 1.0);
}


// Foam texture with black-level smoothstep (Crest: WhiteFoamTexture).
float white_foam_texture(vec2 uv, float foam_amount) {
	float ft = textureLod(foam_texture, uv, 0.0).r;
	float black_point = clamp(1.0 - foam_amount, 0.0, 1.0);
	return smoothstep(black_point, black_point + wave_foam_feather, ft);
}


void fragment() {
	int slice = int(v_slice);
	int c0 = slice;
	int c1 = slice + 1;
	float wt0 = (1.0 - v_lod_alpha) * crest_cascade_weight(c0);
	float wt1 = (1.0 - wt0) * crest_cascade_weight(c1);

	// Clip surface (remove the ocean where registered).
	if (enable_clip_surface > 0.5) {
		float clip_val = 1.0;
		if (wt0 > 0.001) {
			clip_val *= textureLod(ld_clip_surface, crest_world_to_safe_uv(v_world_pos.xz, c0), 0.0).r;
		}
		if (clip_val < 0.5) {
			discard;
		}
	}

	vec3 view = normalize(CAMERA_POSITION_WORLD - v_world_pos);
	bool underwater = !FRONT_FACING;

	// -- Screen depths -------------------------------------------------------
	float pixel_z = -VERTEX.z;
	float raw_depth = textureLod(depth_texture, SCREEN_UV, 0.0).r;
	vec4 upos = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw_depth, 1.0);
	float scene_z = -upos.z / upos.w;

	// -- Normals from displacement (3-tap), dual cascade ---------------------
	vec3 n_pixel = vec3(0.0, 1.0, 0.0);
	float sss_det = 0.0;
	if (wt0 > 0.001) {
		n_pixel.xz += crest_sample_normal(v_world_pos.xz, c0, wt0, sss_det);
	}
	if (wt1 > 0.001) {
		n_pixel.xz += crest_sample_normal(v_world_pos.xz, c1, wt1, sss_det);
	}
	// SSS from the Jacobian determinant (Crest: Ocean.shader remap).
	float sss = sss_det;
	if (slice == int(slice_count) - 1) {
		sss += 1.0 - wt0;
	}
	sss = clamp(0.6 - 0.12 * sss, 0.0, 1.0);

	// -- Detail normal maps ----------------------------------------------------
	float cascade0_scale = cascade_data_a[c0].z;
	{
		// Fade the detail normal map with distance and blur it into higher
		// mips: tiling of the small-scale normal map otherwise reads as
		// banding on calm water from above.
		float normal_fade = clamp(1.0 - pixel_z / 200.0, 0.0, 1.0);
		float mip_bias = log2(max(pixel_z, 1.0)) * 0.5;
		vec2 v0 = vec2(0.94, 0.34);
		vec2 v1 = vec2(-0.85, -0.53);
		float nstretch = normals_scale * cascade0_scale / 100.0;
		float spd = 1.875 * pow(log(1.0 + 2.0 * crest_texel_width(c0)), 1.4);
		vec2 uv0 = (v0 * crest_time * spd + v_undisplaced_xz) / nstretch;
		vec2 uv1 = (v1 * crest_time * spd + v_undisplaced_xz) / nstretch;
		vec2 norm = textureLod(normals_texture, uv0, mip_bias).xy * 2.0 - 1.0
			+ textureLod(normals_texture, uv1, mip_bias).xy * 2.0 - 1.0;
		// Blend with the next-larger scale for distance continuity.
		float nblend = v_lod_alpha;
		if (nblend > 0.001) {
			float nstretch2 = nstretch * 2.0;
			float spd2 = 1.875 * pow(log(1.0 + 4.0 * crest_texel_width(c0)), 1.4);
			vec2 uvb0 = (v0 * crest_time * spd2 + v_undisplaced_xz) / nstretch2;
			vec2 uvb1 = (v1 * crest_time * spd2 + v_undisplaced_xz) / nstretch2;
			vec2 norm2 = textureLod(normals_texture, uvb0, mip_bias).xy * 2.0 - 1.0
				+ textureLod(normals_texture, uvb1, mip_bias).xy * 2.0 - 1.0;
			norm = mix(norm, norm2, nblend);
		}
		n_pixel.xz += normals_strength * normal_fade * norm;
	}
	n_pixel.xz *= normals_strength_overall;
	n_pixel = normalize(n_pixel);
	if (underwater) {
		n_pixel = -n_pixel;
	}

	// -- Sea depth ---------------------------------------------------------------
	float sea_level_offset = v_sea_level_offset;
	float sea_depth = 1000.0;
	if (enable_sea_floor_depth > 0.5) {
		sea_depth = crest_sample_sea_depth(v_world_pos.xz, c0, sea_level_offset);
	}

	// -- Shadow ------------------------------------------------------------------
	vec2 shadow_val = vec2(1.0);
	if (enable_shadow > 0.5) {
		shadow_val = textureLod(ld_shadow, crest_world_to_safe_uv(v_world_pos.xz, c0), 0.0).rg;
	}
	float shadow_soft = shadow_val.r;

	// -- Foam ----------------------------------------------------------------------
	float foam = 0.0;
	if (enable_foam > 0.5) {
		if (wt0 > 0.001) {
			foam += wt0 * textureLod(ld_foam, crest_world_to_safe_uv(v_undisplaced_xz, c0), 0.0).r;
		}
		if (wt1 > 0.001) {
			foam += wt1 * textureLod(ld_foam, crest_world_to_safe_uv(v_undisplaced_xz, c1), 0.0).r;
		}
		foam = clamp(foam, 0.0, 1.0);
	}
	// Shoreline feathering with scene geometry.
	float foam_amount = foam * clamp((scene_z - pixel_z) / shoreline_foam_min_depth, 0.0, 1.0);

	// -- Scattering colour ---------------------------------------------------------
	float v_abs = abs(view.y);
	vec3 col = mix(diffuse_grazing, diffuse, v_abs);
	col = mix(diffuse_shadow, col, shadow_soft);
	float shallowness = pow(1.0 - clamp(sea_depth / subsurface_depth_max, 0.0, 1.0), subsurface_depth_power);
	col = mix(col, subsurface_shallow_col, shallowness);
	// Albedo inputs override the scattering colour.
	if (enable_albedo > 0.5) {
		vec4 alb = textureLod(ld_albedo, crest_world_to_safe_uv(v_world_pos.xz, c0), 0.0);
		col = mix(col, alb.rgb, alb.a);
	}
	col *= ambient_light;

	// SSS: light shining through wave crests.
	float towards_sun = pow(max(0.0, dot(light_dir, -view)), subsurface_sun_falloff);
	vec3 subsurface = (subsurface_base + subsurface_sun * towards_sun) * subsurface_colour * light_color * shadow_soft;
	if (!underwater) {
		subsurface *= (1.0 - v_abs * v_abs) * sss;
	}
	col += subsurface;

	// -- Refraction + depth absorption + caustics -----------------------------------
	float fog_dist = 0.0;
	vec3 scene_colour = vec3(0.0);
	vec3 scene_pos = vec3(0.0);
	float refract_raw_depth = raw_depth;
	{
		vec2 refract_offset = refraction_strength * n_pixel.xz;
		if (!underwater) {
			refract_offset *= min(1.0, 0.5 * (scene_z - pixel_z)) / scene_z;
		}
		vec2 uv_refract = SCREEN_UV + refract_offset;
		float raw2 = textureLod(depth_texture, uv_refract, 0.0).r;
		vec4 upos2 = INV_PROJECTION_MATRIX * vec4(uv_refract * 2.0 - 1.0, raw2, 1.0);
		float scene_z2 = -upos2.z / upos2.w;

		vec2 uv_background = SCREEN_UV;
		if (scene_z2 > pixel_z) {
			// Refracted sample is still below the surface: accept.
			uv_background = uv_refract;
			fog_dist = scene_z2 - pixel_z;
			refract_raw_depth = raw2;
		} else {
			// Refracted onto something in front of the surface: cancel.
			fog_dist = max(scene_z - pixel_z, 0.0);
		}
		scene_colour = textureLod(screen_texture, uv_background, 0.0).rgb;

		// World position of the refracted scene point (for caustics).
		vec3 cam_forward = -VIEW_MATRIX[2].xyz;
		vec3 view_dir = normalize(v_world_pos - CAMERA_POSITION_WORLD);
		scene_pos = CAMERA_POSITION_WORLD + view_dir * (fog_dist + pixel_z) / dot(cam_forward, view_dir);
	}

	// Caustics on the refracted scene colour. Only meaningful where the
	// refracted scene point is actually below the (shallow) sea floor - fade
	// out for distant/deep refractions.
	float caustics_fade = clamp(1.0 - fog_dist / 40.0, 0.0, 1.0);
	if (enable_caustics > 0.5 && !underwater && caustics_fade > 0.001) {
		vec4 disp = crest_sample_displacements(crest_world_to_safe_uv(scene_pos.xz, c0));
		float water_height = ocean_center_pos.y + disp.y + sea_level_offset;
		float scene_depth = water_height - scene_pos.y;
		float mip_lod = log2(max(fog_dist + pixel_z, 1.0)) + abs(scene_depth - caustics_focal_depth) / caustics_depth_of_field;
		vec2 light_proj = light_dir.xz * scene_depth / (4.0 * light_dir.y);
		vec2 cuv1 = (scene_pos.xz + light_proj) / caustics_texture_scale + vec2(0.044 * crest_time + 17.16, -0.169 * crest_time);
		vec2 cuv2 = 1.37 * (scene_pos.xz + light_proj) / caustics_texture_scale + vec2(0.248 * crest_time, 0.117 * crest_time);
		vec2 c_n = caustics_distortion_strength * (texture(normals_texture, scene_pos.xz / caustics_distortion_scale).xy * 2.0 - 1.0);
		cuv1 += 1.30 * c_n;
		cuv2 += 1.77 * c_n;
		vec3 caustics = 0.5 * textureLod(caustics_texture, cuv1, mip_lod).rgb + 0.5 * textureLod(caustics_texture, cuv2, mip_lod).rgb;
		scene_colour *= 1.0 + caustics_strength * (caustics - caustics_texture_average) * shadow_soft * caustics_fade;
	}

	// -- Bubbles (under-surface foam) ---------------------------------------------
	vec3 bubble_col = vec3(0.0);
	if (enable_foam > 0.5) {
		vec2 wind_dir = vec2(0.866, 0.5);
		vec2 uv0 = (mix(v_undisplaced_xz, v_world_pos.xz, 0.7) + 0.5 * crest_time * wind_dir) / foam_scale + 0.125 * n_pixel.xz;
		vec2 parallax = -foam_bubble_parallax * view.xz / max(dot(n_pixel, view), 0.05);
		float ft0 = textureLod(foam_texture, (0.74 * uv0 + parallax) / (cascade0_scale / 25.0), 3.0).r;
		float ft1 = textureLod(foam_texture, (0.74 * uv0 + parallax) / (cascade_data_a[c1].z / 25.0), 3.0).r;
		float ft = mix(ft0, ft1, v_lod_alpha);
		bubble_col = ft * foam_bubble_color * clamp(foam * wave_foam_bubbles_coverage, 0.0, 1.0) * ambient_light;
	}

	// Absorb the scene colour with depth (Beer-Lambert per channel).
	if (!underwater) {
		vec3 absorb = 1.0 - exp(-depth_fog_density * fog_dist);
		col = mix(scene_colour, col + bubble_col, absorb);
	}

	// -- Reflection ------------------------------------------------------------------
	vec3 col_above = col;
	{
		vec3 refl = reflect(-view, n_pixel);
		refl.y = max(refl.y, 0.0);
		float dp = dot(refl, light_dir);
		vec3 sky_colour;
		if (dp > sky_directionality) {
			sky_colour = mix(sky_base, sky_towards_sun, (dp - sky_directionality) / (1.0 - sky_directionality));
		} else {
			sky_colour = mix(sky_away_from_sun, sky_base, (dp + 1.0) / (sky_directionality + 1.0));
		}
		// Planar reflections override the sky.
		if (planar_reflection_intensity > 0.001) {
			vec2 sp = SCREEN_UV + max(1.0, pixel_z * 0.0) * planar_reflection_normals_strength * n_pixel.xz * 0.05;
			vec4 r = textureLod(planar_reflection, sp, 0.0);
			sky_colour = mix(sky_colour, r.rgb, planar_reflection_intensity * clamp(r.a, 0.0, 1.0));
		}
		// Directional light specular.
		sky_colour += pow(max(0.0, dp), directional_light_falloff) * directional_light_boost * light_color * shadow_soft;

		// Schlick fresnel.
		float r0 = pow((1.0 - refractive_index_of_water) / (1.0 + refractive_index_of_water), 2.0);
		float r_theta = r0 + (1.0 - r0) * pow(max(0.0, 1.0 - dot(n_pixel, view)), fresnel_power);
		col = mix(col, sky_colour, r_theta * specular);
	}

	// -- Foam on top ------------------------------------------------------------------
	if (enable_foam > 0.5 && !underwater) {
		float foam_tex_scale = foam_scale * cascade0_scale / 25.0;
		vec2 uv_offset = vec2(crest_time / 10.0);
		float ft0 = white_foam_texture((1.25 * v_undisplaced_xz + uv_offset) / foam_tex_scale, foam_amount);
		float ft1 = white_foam_texture((1.25 * v_undisplaced_xz + uv_offset) / (foam_tex_scale * 2.0), foam_amount);
		float white_foam = mix(ft0, ft1, v_lod_alpha);

		// 3D foam lighting from texture differences.
		vec2 dd = vec2(0.25 * pixel_z / foam_tex_scale, 0.0);
		float dfdx = white_foam_texture((1.25 * (v_undisplaced_xz + dd.xy * foam_tex_scale) + uv_offset) / foam_tex_scale, foam_amount) - ft0;
		float dfdz = white_foam_texture((1.25 * (v_undisplaced_xz + dd.yx * foam_tex_scale) + uv_offset) / foam_tex_scale, foam_amount) - ft0;
		vec3 foam_n = normalize(n_pixel + wave_foam_normal_strength * vec3(-dfdx, 0.0, -dfdz));

		float foam_ndl = max(0.0, dot(foam_n, light_dir));
		vec3 foam_rgb = foam_white_color * (ambient_light + wave_foam_light_scale * light_color * foam_ndl * shadow_soft);
		foam_rgb += pow(max(0.0, dot(reflect(-view, foam_n), light_dir)), wave_foam_specular_falloff)
			* wave_foam_specular_boost * light_color * shadow_soft;
		col = mix(col, foam_rgb, foam_white_alpha * white_foam);
	}

	// -- Underwater backface (Snell's window approximation) ---------------------------
	if (underwater) {
		float cos_out = max(dot(n_pixel, view), 0.0);
		float sin_out = sqrt(max(0.0, 1.0 - cos_out * cos_out));
		float cos_in = cos(asin(clamp(refractive_index_of_water * sin_out, -1.0, 1.0)));
		float r0 = pow((1.0 - refractive_index_of_water) / (1.0 + refractive_index_of_water), 2.0);
		float r_fres = r0 + (1.0 - r0) * pow(max(0.0, 1.0 - cos_in), fresnel_power);
		col = col_above * (1.0 - r_fres) + col_above * r_fres;
	}

	ALBEDO = col;

	// Soften the intersection with scene geometry (20 cm fade).
	ALPHA = clamp((scene_z - pixel_z) / 0.2, 0.0, 1.0);
}
