@tool
extends EditorPlugin
## Registers the Crest node/resource types in the editor.

const _TYPES: Array[Array] = [
	# [class script path, base type, display name]
	["res://addons/crest/core/ocean_renderer.gd", "Node3D", "CrestOceanRenderer"],
	["res://addons/crest/shapes/shape_gerstner.gd", "Node3D", "CrestShapeGerstner"],
	["res://addons/crest/shapes/shape_fft.gd", "Node3D", "CrestShapeFFT"],
	["res://addons/crest/interaction/simple_floating_object.gd", "Node3D", "CrestSimpleFloatingObject"],
	["res://addons/crest/interaction/sphere_water_interaction.gd", "Node3D", "CrestSphereWaterInteraction"],
	["res://addons/crest/interaction/boat_probes.gd", "Node3D", "CrestBoatProbes"],
	["res://addons/crest/underwater/underwater_renderer.gd", "Node3D", "CrestUnderwaterRenderer"],
	["res://addons/crest/simulation/ocean_depth_cache.gd", "Node3D", "CrestOceanDepthCache"],
]


func _enter_tree() -> void:
	for t in _TYPES:
		add_custom_type(t[2], t[1], load(t[0]), null)


func _exit_tree() -> void:
	for t in _TYPES:
		remove_custom_type(t[2])
