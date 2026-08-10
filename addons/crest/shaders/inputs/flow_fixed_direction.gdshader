shader_type spatial;
render_mode unshaded, depth_draw_never, cull_disabled;

// Crest/Inputs/Flow/Fixed Direction. _Direction is turns in [0, 1].
uniform float _Speed = 1.0;
uniform float _Direction = 0.0;
uniform float _FeatherAtUVExtents = 0.0;
uniform float _FeatherWidth = 0.1;

void fragment() { discard; }
