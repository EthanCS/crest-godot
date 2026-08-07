extends Node3D
## Demo scene: spawns floating objects on the ocean and shows controls.

const FloaterScript := preload("res://addons/crest/interaction/simple_floating_object.gd")
const WakeScript := preload("res://addons/crest/interaction/sphere_water_interaction.gd")

@export var floater_count := 8

var _label: Label


func _ready() -> void:
	_label = $CanvasLayer/Label
	_spawn_floaters()
	_spawn_island()
	# Bright-sea material preset for the demo (plugin defaults stay Crest's).
	var ocean := $CrestOceanRenderer as CrestOceanRenderer
	await get_tree().process_frame
	if ocean.ocean_material:
		ocean.ocean_material.set_shader_parameter("diffuse", Vector3(0.0, 0.02, 0.30))
		ocean.ocean_material.set_shader_parameter("diffuse_grazing", Vector3(0.0, 0.03, 0.36))
		ocean.ocean_material.set_shader_parameter("subsurface_shallow_col", Vector3(0.0, 0.08, 0.55))
	if "--showcase" in OS.get_cmdline_user_args():
		_cruise = true
		_label.visible = false


## Builds a shallow-water seabed with an island, with a matching depth
## cache: refraction/caustics/shallow scattering need visible sea floor.
func _spawn_island() -> void:
	var island_center := Vector2(55.0, 0.0)
	var size := 420.0 # world side length of the seabed area
	var res := 513
	var img := Image.create(res, res, false, Image.FORMAT_RF)

	var heights := []
	heights.resize(res * res)
	for j in res:
		for i in res:
			var p := Vector2((float(i) / (res - 1) - 0.5) * size, (float(j) / (res - 1) - 0.5) * size)
			# Island bump (above water) on top of a gently sloping seabed
			# that reaches ~14 m depth at the borders.
			var island_h := 6.0 * exp(-p.length_squared() / (2.0 * 14.0 * 14.0))
			var depth := -1.5 - 5.5 * smoothstep(50.0, 240.0, p.length())
			var h: float = max(island_h - 1.5, depth)
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

	var depth_cache := CrestOceanDepthCache.new()
	depth_cache.heightmap_texture = ImageTexture.create_from_image(img)
	depth_cache.cache_size = Vector2(size, size)
	depth_cache.position = Vector3(island_center.x, 0.0, island_center.y)
	if not "--no-depth" in OS.get_cmdline_user_args():
		add_child(depth_cache)


func _spawn_floaters() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = 42
	var box_mesh := BoxMesh.new()
	box_mesh.size = Vector3(1.2, 1.2, 1.2)
	var sphere_mesh := SphereMesh.new()
	sphere_mesh.radius = 0.7
	sphere_mesh.height = 1.4

	var mats := [
		_make_mat(Color(0.9, 0.3, 0.25)),
		_make_mat(Color(0.95, 0.75, 0.2)),
		_make_mat(Color(0.3, 0.55, 0.9)),
	]

	for i in floater_count:
		var body := RigidBody3D.new()
		body.mass = 2.0
		var angle := TAU * i / floater_count
		var dist := rng.randf_range(6.0, 22.0)
		body.position = Vector3(cos(angle) * dist, 3.0 + i * 0.5, sin(angle) * dist)
		add_child(body)

		var shape := CollisionShape3D.new()
		var is_box := i % 2 == 0
		if is_box:
			var bs := BoxShape3D.new()
			bs.size = Vector3(1.2, 1.2, 1.2)
			shape.shape = bs
		else:
			var ss := SphereShape3D.new()
			ss.radius = 0.7
			shape.shape = ss
		body.add_child(shape)

		var mi := MeshInstance3D.new()
		if is_box:
			mi.mesh = box_mesh
		else:
			mi.mesh = sphere_mesh
		mi.material_override = mats[i % mats.size()]
		body.add_child(mi)

		var floater := Node3D.new()
		floater.set_script(FloaterScript)
		floater.set("object_width", 1.4)
		# Weaker wake response in the open-ocean demo (Crest default seas).
		floater.set("raise_object", 0.8)
		body.add_child(floater)

		var wake := Node3D.new()
		wake.set_script(WakeScript)
		wake.set("radius", 0.6)
		wake.set("weight", 0.35)
		wake.set("foam_strength", 0.3)
		body.add_child(wake)


func _make_mat(color: Color) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = color
	m.roughness = 0.6
	return m


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and event.keycode == KEY_U:
		var cam := $Camera3D
		cam.position.y = -3.0 if cam.position.y > 0.0 else 15.0
	elif event is InputEventKey and event.pressed and event.keycode == KEY_C:
		_cruise = not _cruise


# -- Showcase cruise ----------------------------------------------------------
# Camera path for the feature showcase video (press C to toggle).

const CRUISE_POINTS := [
	# [position, look_at, seconds] - open ocean, island, floaters, underwater,
	# low-angle reflections, outro.
	[Vector3(0, 20, 70), Vector3(0, 0, 0), 6.0],
	[Vector3(28, 10, 28), Vector3(58, 1, 0), 6.0],
	[Vector3(-4, 5, -18), Vector3(0, 0, 4), 6.0],
	[Vector3(0, -2.5, 26), Vector3(0, 0, -10), 6.0],
	[Vector3(6, 2.5, 10), Vector3(55, 2, 0), 6.0],
	[Vector3(0, 22, 75), Vector3(0, 0, 0), 5.0],
]

var _cruise := false
var _cruise_time := 0.0


func _cruise_total() -> float:
	var t := 0.0
	for p in CRUISE_POINTS:
		t += p[2]
	return t


func _update_cruise(delta: float) -> void:
	_cruise_time += delta
	var t := fmod(_cruise_time, _cruise_total())
	var acc := 0.0
	for i in CRUISE_POINTS.size():
		var dur: float = CRUISE_POINTS[i][2]
		if t < acc + dur:
			var a: float = smoothstep(0.0, 1.0, (t - acc) / dur)
			var nxt := (i + 1) % CRUISE_POINTS.size()
			var cam := $Camera3D
			cam.position = (CRUISE_POINTS[i][0] as Vector3).lerp(CRUISE_POINTS[nxt][0], a)
			var look := (CRUISE_POINTS[i][1] as Vector3).lerp(CRUISE_POINTS[nxt][1], a)
			cam.look_at(look, Vector3.UP)
			return
		acc += dur


func _process(delta: float) -> void:
	if _cruise:
		_update_cruise(delta)
	if _label:
		var ocean := CrestOceanRenderer.instance
		var fps := Engine.get_frames_per_second()
		_label.text = "Crest Ocean System for Godot — demo\nWASD/QE + mouse: fly | Shift: fast | U: dive/surface | F9: debug overlay | Esc: release mouse\nFPS: %d | scale: %d | time: %.1f" % [fps, int(ocean.ocean_scale) if ocean else 0, ocean.current_time() if ocean else 0.0]
