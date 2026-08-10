extends Node3D
## Demo scene: spawns floating objects on the ocean and shows controls.

const FloaterScript := preload("res://addons/crest/csharp/CrestSimpleFloatingObject.cs")
const WakeScript := preload("res://addons/crest/csharp/CrestSphereWaterInteraction.cs")
const DepthCacheScript := preload("res://addons/crest/csharp/CrestOceanDepthCache.cs")
const BoatScript := preload("res://demo/CrestDemoBoat.cs")

@export var floater_count := 8
@export var spawn_demo_boat := true
@export var spawn_shallow_seabed := true

var _label: Label


func _ready() -> void:
	_label = $CanvasLayer/Label
	# Keep the demo root (and with it the camera + HUD) processing while the
	# tree is paused; pause-able systems are opted out individually below.
	process_mode = Node.PROCESS_MODE_ALWAYS
	$CrestOceanRenderer.process_mode = Node.PROCESS_MODE_PAUSABLE
	$CrestOceanDebugGui.process_mode = Node.PROCESS_MODE_PAUSABLE
	if spawn_shallow_seabed:
		_spawn_shallow_seabed()
	if spawn_demo_boat and not "--input-showcase" in OS.get_cmdline_user_args():
		_spawn_boat()
	if "--debug-gui" in OS.get_cmdline_user_args():
		for i in 15:
			await get_tree().process_frame
		$CrestOceanDebugGui.SetOverlayVisible(true)
	# Bright-sea material preset for the demo (plugin defaults stay Crest's).
	# The scene-facing node is now the C# facade; its material property mirrors
	# the legacy backend during the incremental runtime migration.
	var ocean = $CrestOceanRenderer
	await get_tree().process_frame
	if ocean._material:
		ocean._material.set_shader_parameter("diffuse", Vector3(0.0, 0.02, 0.30))
		ocean._material.set_shader_parameter("diffuse_grazing", Vector3(0.0, 0.03, 0.36))
		ocean._material.set_shader_parameter("subsurface_shallow_col", Vector3(0.0, 0.08, 0.55))
		# Caustics tuned for the ~3 m deep pond.
		ocean._material.set_shader_parameter("caustics_focal_depth", 3.0)
		ocean._material.set_shader_parameter("caustics_depth_of_field", 1.2)
		ocean._material.set_shader_parameter("caustics_strength", 3.5)


## Builds a shallow sandy seabed with gentle ripples, with a matching
## depth cache: refraction/caustics/shallow scattering need visible floor.
## Sized well beyond the demo area so the terrain edge never reads as an
## ocean artefact from any reasonable viewpoint.
func _spawn_shallow_seabed() -> void:
	var island_center := Vector2(0.0, 0.0)
	var size := 1200.0 # world side length of the seabed area
	var res := 513
	var img := Image.create(res, res, false, Image.FORMAT_RF)

	var heights := []
	heights.resize(res * res)
	for j in res:
		for i in res:
			var p := Vector2((float(i) / (res - 1) - 0.5) * size, (float(j) / (res - 1) - 0.5) * size)
			# Mostly flat sand at ~3.2 m with soft bumps, sloping down far out.
			var h := -3.2
			h -= 8.0 * smoothstep(300.0, 420.0, p.length())
			heights[j * res + i] = h
			img.set_pixel(i, j, Color(h, 0, 0))

	# Terrain mesh matching the heightmap.
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for j in res - 1:
		for i in res - 1:
			var x0 := (float(i) / (res - 1) - 0.5) * size
			var z0 := (float(j) / (res - 1) - 0.5) * size
			var x1 := (float(i + 1) / (res - 1) - 0.5) * size
			var z1 := (float(j + 1) / (res - 1) - 0.5) * size
			var h00: float = heights[j * res + i]
			var h10: float = heights[j * res + i + 1]
			var h01: float = heights[(j + 1) * res + i]
			var h11: float = heights[(j + 1) * res + i + 1]
			var v00 := Vector3(x0, h00, z0)
			var v10 := Vector3(x1, h10, z0)
			var v01 := Vector3(x0, h01, z1)
			var v11 := Vector3(x1, h11, z1)
			# Godot front-face order seen from above (+Y), same as the ocean
			# patches: (0,0) -> (1,0) -> (0,1). The shading normal attribute
			# is independent and points up.
			var n := (v01 - v00).cross(v10 - v00).normalized()
			st.set_normal(n)
			st.add_vertex(v00)
			st.set_normal(n)
			st.add_vertex(v10)
			st.set_normal(n)
			st.add_vertex(v01)
			st.set_normal(n)
			st.add_vertex(v10)
			st.set_normal(n)
			st.add_vertex(v11)
			st.set_normal(n)
			st.add_vertex(v01)
	st.generate_normals()
	var terrain := MeshInstance3D.new()
	terrain.mesh = st.commit()
	# The seabed would show shadow-map banding through the refracted water.
	terrain.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(0.76, 0.70, 0.50)
	mat.roughness = 0.9
	terrain.material_override = mat
	terrain.position = Vector3(island_center.x, 0.0, island_center.y)
	add_child(terrain)

	var depth_cache := DepthCacheScript.new()
	depth_cache.SetBakedCache(ImageTexture.create_from_image(img), Vector2(size, size))
	depth_cache.position = Vector3(island_center.x, 0.0, island_center.y)
	if not "--no-depth" in OS.get_cmdline_user_args():
		add_child(depth_cache)


## Spawns the little boat that circles the pond and stirs ripples.
func _spawn_boat() -> void:
	var boat := Node3D.new()
	boat.set_script(BoatScript)
	boat.set("radius", 12.0)
	boat.set("speed", 2.0)
	boat.position = Vector3(12.0, 0.0, 0.0)
	boat.process_mode = Node.PROCESS_MODE_PAUSABLE
	add_child(boat)

	var hull := MeshInstance3D.new()
	var hull_mesh := BoxMesh.new()
	hull_mesh.size = Vector3(4.2, 0.7, 1.8)
	hull.mesh = hull_mesh
	hull.material_override = _make_mat(Color(0.45, 0.28, 0.15))
	boat.add_child(hull)

	var cabin := MeshInstance3D.new()
	var cabin_mesh := BoxMesh.new()
	cabin_mesh.size = Vector3(1.4, 0.8, 1.2)
	cabin.mesh = cabin_mesh
	cabin.material_override = _make_mat(Color(0.9, 0.88, 0.82))
	cabin.position = Vector3(-0.6, 0.7, 0.0)
	boat.add_child(cabin)

	# Wake generator: dynamic wave ripples + a bit of foam.
	var wake = WakeScript.new()
	wake._radius = 2.5
	# d32f43c demo tuning: intentionally much stronger than the component
	# default, producing the large visible wake used by the reference scene.
	wake._weight = 30.0
	wake.SetFoamStrength(0.7)
	wake._teleportSpeed = 720.0
	boat.add_child(wake)

func _make_mat(color: Color) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = color
	m.roughness = 0.6
	return m


func _unhandled_input(event: InputEvent) -> void:
	# Space: freeze the ocean/boat (camera + HUD keep running so the frozen
	# moment can be inspected from any angle; the label shows the frozen
	# ocean time and the live camera pose).
	if event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_SPACE:
		get_tree().paused = not get_tree().paused


func _process(_delta: float) -> void:
	if _label:
		var ocean = $CrestOceanRenderer
		var cam := get_viewport().get_camera_3d()
		var fps := Engine.get_frames_per_second()
		var cam_info := ""
		if cam:
			var p := cam.global_position
			var r := cam.global_rotation
			cam_info = "cam=(%.1f,%.1f,%.1f) pitch=%.0f yaw=%.0f" % [p.x, p.y, p.z, rad_to_deg(r.x), rad_to_deg(r.y)]
		var paused_tag := " | PAUSED (Space)" if get_tree().paused else ""
		_label.text = "Crest Ocean System for Godot — demo\nWASD/QE + mouse: fly | Shift: fast | U: dive/surface | Space: pause | F9: debug overlay | Esc: release mouse\nFPS: %d | scale: %d | time: %.1f | lodAlpha: %.2f%s\n%s" % [fps, int(ocean.GetOceanScale()) if ocean else 0, ocean.GetCurrentTime() if ocean else 0.0, ocean.GetViewerAltitudeLevelAlpha() if ocean else 0.0, paused_tag, cam_info]
