extends Node3D
## Demo scene: spawns floating objects on the ocean and shows controls.

const FloaterScript := preload("res://addons/crest/interaction/simple_floating_object.gd")
const WakeScript := preload("res://addons/crest/interaction/sphere_water_interaction.gd")

@export var floater_count := 8

var _label: Label


func _ready() -> void:
	_label = $CanvasLayer/Label
	_spawn_floaters()


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
		var mesh: Mesh = box_mesh if is_box else sphere_mesh
		mi.mesh = mesh
		mi.material_override = mats[i % mats.size()]
		body.add_child(mi)

		var floater := Node3D.new()
		floater.set_script(FloaterScript)
		floater.set("object_width", 1.4)
		body.add_child(floater)

		var wake := Node3D.new()
		wake.set_script(WakeScript)
		wake.set("radius", 0.8)
		wake.set("foam_strength", 0.8)
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
