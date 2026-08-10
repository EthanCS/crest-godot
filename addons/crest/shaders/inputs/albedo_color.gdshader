shader_type spatial;
render_mode unshaded, depth_draw_never, cull_disabled;

// Crest/Inputs/Albedo/Color
uniform sampler2D _Texture : source_color, filter_linear, repeat_enable;
uniform vec4 _Texture_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 _Color : source_color = vec4(1.0);
uniform float _Cutoff = 0.5;
uniform int _BlendModeSource = 5;
uniform int _BlendModeTarget = 10;

void fragment() { discard; }
