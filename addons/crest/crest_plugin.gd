@tool
extends EditorPlugin
## Registers the Crest node/resource types in the editor.

const _TYPES: Array[Array] = [
	# [class script path, base type, display name]
	["res://addons/crest/core/ocean_renderer.gd", "Node3D", "CrestOceanRenderer"],
	["res://addons/crest/core/ocean_debug_gui.gd", "Node3D", "CrestOceanDebugGui"],
	["res://addons/crest/core/floating_origin.gd", "Node3D", "CrestFloatingOrigin"],
	["res://addons/crest/shapes/shape_gerstner.gd", "Node3D", "CrestShapeGerstner"],
	["res://addons/crest/shapes/shape_fft.gd", "Node3D", "CrestShapeFFT"],
	["res://addons/crest/interaction/simple_floating_object.gd", "Node3D", "CrestSimpleFloatingObject"],
	["res://addons/crest/interaction/sphere_water_interaction.gd", "Node3D", "CrestSphereWaterInteraction"],
	["res://addons/crest/interaction/boat_probes.gd", "Node3D", "CrestBoatProbes"],
	["res://addons/crest/underwater/underwater_renderer.gd", "Node3D", "CrestUnderwaterRenderer"],
	["res://addons/crest/simulation/ocean_depth_cache.gd", "Node3D", "CrestOceanDepthCache"],
	["res://addons/crest/reflection/ocean_planar_reflection.gd", "Node3D", "CrestOceanPlanarReflection"],
	["res://addons/crest/inputs/register_foam_input.gd", "Node3D", "CrestRegisterFoamInput"],
	["res://addons/crest/inputs/register_flow_input.gd", "Node3D", "CrestRegisterFlowInput"],
	["res://addons/crest/inputs/register_sea_floor_depth_input.gd", "Node3D", "CrestRegisterSeaFloorDepthInput"],
	["res://addons/crest/inputs/register_clip_surface_input.gd", "Node3D", "CrestRegisterClipSurfaceInput"],
	["res://addons/crest/inputs/register_albedo_input.gd", "Node3D", "CrestRegisterAlbedoInput"],
	["res://addons/crest/inputs/register_shadow_input.gd", "Node3D", "CrestRegisterShadowInput"],
	["res://addons/crest/inputs/register_anim_waves_input.gd", "Node3D", "CrestRegisterAnimWavesInput"],
]


var _icon: Texture2D


func _enter_tree() -> void:
	_icon = load("res://addons/crest/icons/ocean.svg")
	for t in _TYPES:
		add_custom_type(t[2], t[1], load(t[0]), _icon)


func _exit_tree() -> void:
	for t in _TYPES:
		remove_custom_type(t[2])
