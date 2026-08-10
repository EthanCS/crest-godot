shader_type spatial;
render_mode unshaded, depth_draw_never, cull_disabled;

// Crest/Inputs/Clip Surface/Remove Area has no Material properties.
void fragment() { discard; }
