extends Node3D

const COLORS: Array[Color] = [Color("#ff5d73"), Color("#55a7ff"), Color("#ffc857"), Color("#68e0a3"), Color("#b889ff"), Color("#ff914d"), Color("#55d8d0"), Color("#f47cc4")]
const NAMES := ["赤焰", "苍蓝", "金戈", "青岚", "紫霄", "烈阳", "碧海", "绯月"]
const ARENA := 24.0
var faction_count := 4
var armies: Array[Dictionary] = []
var units: Array[Dictionary] = []
var running := false
var elapsed := 0.0
var spawn_clock := 0.0
var selected_count: OptionButton
var status_label: Label
var score_label: Label
var camera_pivot: Node3D
var dragging := false

func _ready() -> void:
	_build_world()
	_build_camera()
	_build_ui()

func _build_world() -> void:
	var world := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(ARENA * 2.0, ARENA * 2.0)
	world.mesh = plane
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color("#18252b")
	mat.roughness = 0.95
	world.material_override = mat
	add_child(world)
	for i in range(13):
		var line := MeshInstance3D.new()
		var mesh := BoxMesh.new()
		mesh.size = Vector3(0.025, 0.012, ARENA * 2.0)
		line.mesh = mesh
		line.position = Vector3(-ARENA + i * 4.0, 0.012, 0)
		var lm := StandardMaterial3D.new()
		lm.albedo_color = Color("#293940")
		line.material_override = lm
		add_child(line)
		var line2 := MeshInstance3D.new()
		line2.mesh = mesh
		line2.rotation.y = PI / 2
		line2.position = Vector3(0, 0.013, -ARENA + i * 4.0)
		line2.material_override = lm
		add_child(line2)
	var env := WorldEnvironment.new()
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color("#070d14")
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color("#a9bed0")
	environment.ambient_light_energy = 0.45
	env.environment = environment
	add_child(env)
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-55, -25, 0)
	light.light_energy = 1.4
	add_child(light)

func _build_camera() -> void:
	camera_pivot = Node3D.new()
	camera_pivot.position = Vector3(0, 0, 0)
	add_child(camera_pivot)
	var camera := Camera3D.new()
	camera.position = Vector3(0, 33, 32)
	camera.look_at(Vector3.ZERO)
	camera.fov = 52
	camera_pivot.add_child(camera)

func _build_ui() -> void:
	var layer := CanvasLayer.new()
	add_child(layer)
	var panel := PanelContainer.new()
	panel.position = Vector2(24, 24)
	panel.custom_minimum_size = Vector2(330, 0)
	panel.add_theme_stylebox_override("panel", _panel_style(Color("#e8101822"), Color("#405363")))
	layer.add_child(panel)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 12)
	box.add_theme_constant_override("margin_left", 18)
	box.add_theme_constant_override("margin_right", 18)
	box.add_theme_constant_override("margin_top", 16)
	box.add_theme_constant_override("margin_bottom", 16)
	panel.add_child(box)
	var title := Label.new()
	title.text = "LONG FIGHT  /  自动战场"
	title.add_theme_font_size_override("font_size", 23)
	box.add_child(title)
	var subtitle := Label.new()
	subtitle.text = "势力自行扩军、遭遇与争夺战场"
	subtitle.add_theme_color_override("font_color", Color("#9aafbe"))
	box.add_child(subtitle)
	var row := HBoxContainer.new()
	box.add_child(row)
	var count_label := Label.new()
	count_label.text = "参战势力"
	count_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(count_label)
	selected_count = OptionButton.new()
	for n in range(2, 9): selected_count.add_item("%d 支" % n, n)
	selected_count.select(2)
	row.add_child(selected_count)
	var start := Button.new()
	start.text = "开始自动战斗"
	start.custom_minimum_size = Vector2(0, 42)
	start.pressed.connect(_start_battle)
	box.add_child(start)
	status_label = Label.new()
	status_label.text = "选择势力数量，开始后战斗将持续进行。"
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	status_label.add_theme_color_override("font_color", Color("#b3c2ce"))
	box.add_child(status_label)
	score_label = Label.new()
	score_label.text = ""
	box.add_child(score_label)
	var tip := Label.new()
	tip.text = "拖动鼠标旋转视角 · 滚轮缩放 · 空格暂停/继续"
	tip.add_theme_font_size_override("font_size", 12)
	tip.add_theme_color_override("font_color", Color("#8296a5"))
	box.add_child(tip)

func _panel_style(bg: Color, border: Color) -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = bg
	s.border_color = border
	s.set_border_width_all(1)
	s.set_corner_radius_all(12)
	s.content_margin_left = 8
	s.content_margin_right = 8
	s.content_margin_top = 8
	s.content_margin_bottom = 8
	return s

func _start_battle() -> void:
	for unit in units:
		if is_instance_valid(unit.node): unit.node.queue_free()
	units.clear()
	armies.clear()
	faction_count = selected_count.get_selected_id()
	elapsed = 0.0
	spawn_clock = 0.0
	running = true
	for i in range(faction_count):
		var angle := TAU * float(i) / faction_count - PI / 2
		var pos := Vector3(cos(angle) * 17.0, 0, sin(angle) * 17.0)
		armies.append({"name": NAMES[i], "color": COLORS[i], "home": pos, "kills": 0, "alive": true, "units": 0})
		_add_marker(pos, COLORS[i], NAMES[i])
	status_label.text = "战斗进行中 · 各势力正在向战场中心推进"

func _add_marker(pos: Vector3, color: Color, label_text: String) -> void:
	var marker := MeshInstance3D.new()
	var mesh := CylinderMesh.new()
	mesh.top_radius = 1.35
	mesh.bottom_radius = 1.55
	mesh.height = 0.1
	marker.mesh = mesh
	marker.position = pos + Vector3(0, 0.06, 0)
	var material := StandardMaterial3D.new()
	material.albedo_color = color.darkened(0.5)
	material.emission_enabled = true
	material.emission = color.darkened(0.35)
	marker.material_override = material
	add_child(marker)
	var label := Label3D.new()
	label.text = label_text
	label.position = pos + Vector3(0, 0.3, 0)
	label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	label.font_size = 48
	label.modulate = color
	add_child(label)

func _process(delta: float) -> void:
	if not running: return
	elapsed += delta
	spawn_clock += delta
	if spawn_clock >= 0.72:
		spawn_clock = 0
		_spawn_round()
	_simulate(delta)
	_update_score()

func _spawn_round() -> void:
	for i in range(armies.size()):
		if not armies[i].alive or armies[i].units >= 34: continue
		var home: Vector3 = armies[i].home
		var target := Vector3(-home.x * 0.25, 0, -home.z * 0.25)
		var dir := (target - home).normalized()
		var side := Vector3(-dir.z, 0, dir.x)
		var offset := side * randf_range(-2.7, 2.7) + dir * randf_range(-0.5, 1.3)
		var spawn_pos: Vector3 = home + offset
		var node := _make_soldier(armies[i].color)
		add_child(node)
		node.position = spawn_pos
		units.append({"node": node, "faction": i, "hp": 3.0, "speed": randf_range(1.4, 2.2), "attack": 0.0, "phase": randf() * TAU})
		armies[i].units += 1

func _make_soldier(color: Color) -> Node3D:
	var root := Node3D.new()
	var body := MeshInstance3D.new()
	var mesh := CapsuleMesh.new()
	mesh.radius = 0.27
	mesh.height = 0.9
	body.mesh = mesh
	body.position.y = 0.52
	var m := StandardMaterial3D.new()
	m.albedo_color = color
	m.metallic = 0.22
	m.roughness = 0.4
	m.emission_enabled = true
	m.emission = color.darkened(0.75)
	body.material_override = m
	root.add_child(body)
	var head := MeshInstance3D.new()
	var sphere := SphereMesh.new()
	sphere.radius = 0.2
	sphere.height = 0.4
	head.mesh = sphere
	head.position.y = 1.08
	head.material_override = m
	root.add_child(head)
	return root

func _simulate(delta: float) -> void:
	var remove_indices: Array[int] = []
	for i in range(units.size()):
		var u: Dictionary = units[i]
		var node: Node3D = u.node
		if not is_instance_valid(node):
			remove_indices.append(i)
			continue
		var faction: int = u.faction
		var nearest: int = -1
		var dist := 999.0
		for j in range(units.size()):
			if i == j or units[j].faction == faction: continue
			var d: float = node.position.distance_to(units[j].node.position)
			if d < dist:
				dist = d
				nearest = j
		if nearest >= 0 and dist < 1.5:
			u.attack = float(u.attack) - delta
			if float(u.attack) <= 0:
				u.attack = randf_range(0.55, 0.9)
				units[nearest].hp = float(units[nearest].hp) - 1.0
				if float(units[nearest].hp) <= 0:
					armies[faction].kills += 1
			else:
			var target_pos: Vector3 = units[nearest].node.position
			var direction := (target_pos - node.position).normalized()
			node.position += direction * float(u.speed) * delta
			node.look_at(Vector3(target_pos.x, node.position.y, target_pos.z))
		else:
			var home: Vector3 = armies[faction].home
			var center_pull := (Vector3.ZERO - node.position).normalized()
			node.position += center_pull * float(u.speed) * 0.55 * delta
			node.position.y = 0
		units[i] = u
	for i in range(remove_indices.size() - 1, -1, -1): units.remove_at(remove_indices[i])
	for i in range(units.size() - 1, -1, -1):
		if float(units[i].hp) <= 0:
			if is_instance_valid(units[i].node): units[i].node.queue_free()
			units.remove_at(i)
	var alive_count := 0
	for army in armies:
		army.units = 0
	for u in units: armies[u.faction].units += 1
	for army in armies:
		if army.units > 0: alive_count += 1
	if alive_count <= 1:
		running = false
		status_label.text = "战斗结束 · %s 获胜" % _winner_name()

func _winner_name() -> String:
	var best := -1
	for i in range(armies.size()):
		if armies[i].units > 0: best = i
	return "无人" if best < 0 else str(armies[best].name)

func _update_score() -> void:
	var lines: PackedStringArray = []
	for army in armies:
		lines.append("[color=#%s]%s[/color]  %d 人  ·  击败 %d" % [army.color.to_html(false), army.name, army.units, army.kills])
	score_label.text = "\n".join(lines) + "\n\n战场时间  %02d:%02d" % [int(elapsed) / 60, int(elapsed) % 60]

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and event.keycode == KEY_SPACE and not event.echo:
		running = not running
		if not armies.is_empty(): status_label.text = "战斗进行中" if running else "战斗已暂停 · 按空格继续"
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT: dragging = event.pressed
		if event.pressed and event.button_index == MOUSE_BUTTON_WHEEL_UP:
			camera_pivot.get_child(0).position *= 0.9
		if event.pressed and event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			camera_pivot.get_child(0).position *= 1.1
	if event is InputEventMouseMotion and dragging:
		camera_pivot.rotate_y(-event.relative.x * 0.005)
