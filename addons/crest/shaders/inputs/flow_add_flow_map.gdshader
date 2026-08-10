shader_type spatial;
render_mode unshaded, depth_draw_never, cull_disabled;

// Crest/Inputs/Flow/Add Flow Map
uniform sampler2D _FlowMap : filter_linear, repeat_enable;
uniform vec4 _FlowMap_ST = vec4(1.0, 1.0, 0.0, 0.0);
uniform float _Strength = 1.0;
uniform float _FlipX = 0.0;
uniform float _FlipZ = 0.0;
uniform float _FeatherAtUVExtents = 0.0;
uniform float _FeatherWidth = 0.1;

void fragment() { discard; }
