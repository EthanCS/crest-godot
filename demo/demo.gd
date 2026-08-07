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


## Builds a small island (gaussian bump) with a matching depth cache so the
## shoreline foam / shallow water colours show up.
func _spawn_island() -> void:
	var island_center := Vector2(55.0, 0.0)
	var size := 90.0 # world side length of the island area
	var res := 129
	var img := Image.create(res, res, false, Image.FORMAT_RF)

	var heights := []
	heights.resize(res * res)
	for j in res:
		for i in res:
			var p := Vector2((float(i) / (res - 1) - 0.5) * size, (float(j) / (res - 1) - 0.5) * size)
			var d := p.length() / (size * 0.5)
			# Gaussian island peak above water, sloping down to deep water.
			var h := 6.0 * exp(-d * d * 3.0) - 1.2
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
	var terrain := MeshInstance3D.new()
	terrain.mesh = st.commit()
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(0.55, 0.48, 0.35)
	mat.roughness = 0.9
	terrain.material_override = mat
	terrain.position = Vector3(island_center.x, 0.0, island_center.y)
	add_child(terrain)

	var depth_cache := CrestOceanDepthCache.new()
	depth_cache.heightmap_texture = ImageTexture.create_from_image(img)
	depth_cache.cache_size = Vector2(size, size)
	depth_cache.position = Vector3(island_center.x, 0.0, island_center.y)
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


func _process(_delta: float) -> void:
	if _label:
		var ocean := CrestOceanRenderer.instance
		var fps := Engine.get_frames_per_second()
		_label.text = "Crest Ocean System for Godot — demo\nWASD/QE + mouse: fly | Shift: fast | Esc: release mouse\nFPS: %d | scale: %d | time: %.1f" % [fps, int(ocean.ocean_scale) if ocean else 0, ocean.current_time() if ocean else 0.0]
