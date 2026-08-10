shader_type spatial;
render_mode unshaded, depth_draw_never, cull_disabled;

// Crest/Inputs/Depth/Cached Depths
uniform sampler2D _MainTex : filter_linear, repeat_enable;
uniform vec4 _MainTex_ST = vec4(1.0, 1.0, 0.0, 0.0);

void fragment() { discard; }
