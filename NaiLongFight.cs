using Godot;
using System;
using System.Collections.Generic;

public partial class NaiLongFight : Node3D
{
    private static readonly Color[] Colors = [new("#ff5d73"), new("#55a7ff"), new("#ffc857"), new("#68e0a3"), new("#b889ff"), new("#ff914d"), new("#55d8d0"), new("#f47cc4")];
    private static readonly string[] Names = ["赤焰", "苍蓝", "金戈", "青岚", "紫霄", "烈阳", "碧海", "绯月"];
    private const float Arena = 24f;
    private readonly List<Army> _armies = [];
    private readonly List<Soldier> _units = [];
    private readonly RandomNumberGenerator _rng = new();
    private readonly FastNoiseLite _terrainNoise = new();
    private Node3D _terrainRoot = null!;
    private Node3D _markersRoot = null!;
    private OptionButton _count = null!;
    private Label _status = null!;
    private Label _score = null!;
    private Node3D _cameraPivot = null!;
    private bool _running;
    private bool _dragging;
    private float _elapsed;
    private float _spawnClock;

    private sealed class Army(int id, string name, Color color, Vector3 home)
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
        public Color Color { get; } = color;
        public Vector3 Home { get; } = home;
        public int Kills;
        public int UnitCount;
    }

    private sealed class Soldier(Node3D node, int faction, float speed, float phase)
    {
        public Node3D Node { get; } = node;
        public int Faction { get; } = faction;
        public float Speed { get; } = speed;
        public float Phase { get; } = phase;
        public float Hp = 3f;
        public float Attack;
    }

    public override void _Ready()
    {
        _rng.Randomize();
        BuildWorld(); BuildCamera(); BuildUi();
    }

    private void BuildWorld()
    {
        _terrainRoot = new Node3D { Name = "Terrain" }; AddChild(_terrainRoot);
        _markersRoot = new Node3D { Name = "FactionMarkers" }; AddChild(_markersRoot);
        GenerateTerrain();
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("#070d14"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color("#a9bed0"), AmbientLightEnergy = .45f };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -25, 0), LightEnergy = 1.4f });
    }

    private float HeightAt(float x, float z)
    {
        float broad = _terrainNoise.GetNoise2D(x, z) * 3.4f;
        float detail = _terrainNoise.GetNoise2D(x * 2.7f + 43.1f, z * 2.7f - 17.8f) * .7f;
        float ridge = Mathf.Max(0, _terrainNoise.GetNoise2D(x * .55f - 11, z * .55f + 29)) * 1.5f;
        return broad + detail + ridge;
    }

    private void GenerateTerrain()
    {
        foreach (var child in _terrainRoot.GetChildren()) child.QueueFree();
        _terrainNoise.Seed = (int)_rng.Randi(); _terrainNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        _terrainNoise.Frequency = .045f; _terrainNoise.FractalOctaves = 4; _terrainNoise.FractalLacunarity = 2.1f;
        const int resolution = 112;
        var vertices = new Vector3[(resolution + 1) * (resolution + 1)];
        var colors = new Color[vertices.Length];
        var indices = new int[resolution * resolution * 6];
        for (int z = 0; z <= resolution; z++)
        for (int x = 0; x <= resolution; x++)
        {
            float wx = -Arena + 2 * Arena * x / resolution;
            float wz = -Arena + 2 * Arena * z / resolution;
            float h = HeightAt(wx, wz);
            int index = z * (resolution + 1) + x;
            vertices[index] = new Vector3(wx, h, wz);
            float shade = _terrainNoise.GetNoise2D(wx * 1.9f, wz * 1.9f) * .07f;
            Color baseColor = h > 2.0f ? new Color("#827b69") : h < -1.5f ? new Color("#344b4a") : new Color("#49634b");
            colors[index] = new Color(Mathf.Clamp(baseColor.R + shade, 0, 1), Mathf.Clamp(baseColor.G + shade, 0, 1), Mathf.Clamp(baseColor.B + shade, 0, 1));
            if (x == resolution || z == resolution) continue;
            int a = index, b = index + resolution + 1, c = index + 1, d = b + 1;
            int t = (z * resolution + x) * 6;
            indices[t] = a; indices[t + 1] = b; indices[t + 2] = c;
            indices[t + 3] = c; indices[t + 4] = b; indices[t + 5] = d;
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.Color] = colors; arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .96f });
        _terrainRoot.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    private void BuildCamera()
    {
        _cameraPivot = new Node3D(); AddChild(_cameraPivot);
        var camera = new Camera3D { Position = new Vector3(0, 33, 32), Fov = 52 };
        _cameraPivot.AddChild(camera); camera.LookAt(Vector3.Zero);
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer(); AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(24, 24), CustomMinimumSize = new Vector2(330, 0) };
        var style = new StyleBoxFlat { BgColor = new Color("#e8101822"), BorderColor = new Color("#405363"), ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 16, ContentMarginBottom = 16 };
        style.SetBorderWidthAll(1); style.SetCornerRadiusAll(12); panel.AddThemeStyleboxOverride("panel", style); layer.AddChild(panel);
        var box = new VBoxContainer { Theme = null }; box.AddThemeConstantOverride("separation", 12); panel.AddChild(box);
        var title = new Label { Text = "LONG FIGHT  /  自动战场" }; title.AddThemeFontSizeOverride("font_size", 23); box.AddChild(title);
        var subtitle = new Label { Text = "势力自行扩军、遭遇与争夺战场" }; subtitle.AddThemeColorOverride("font_color", new Color("#9aafbe")); box.AddChild(subtitle);
        var row = new HBoxContainer(); box.AddChild(row);
        row.AddChild(new Label { Text = "参战势力", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _count = new OptionButton(); for (int n = 2; n <= 8; n++) _count.AddItem($"{n} 支", n); _count.Select(2); row.AddChild(_count);
        var start = new Button { Text = "开始自动战斗", CustomMinimumSize = new Vector2(0, 42) }; start.Pressed += StartBattle; box.AddChild(start);
        _status = new Label { Text = "选择势力数量，开始后战斗将持续进行。", AutowrapMode = TextServer.AutowrapMode.WordSmart }; _status.AddThemeColorOverride("font_color", new Color("#b3c2ce")); box.AddChild(_status);
        _score = new Label(); box.AddChild(_score);
        var tip = new Label { Text = "拖动鼠标旋转视角 · 滚轮缩放 · 空格暂停/继续" }; tip.AddThemeFontSizeOverride("font_size", 12); tip.AddThemeColorOverride("font_color", new Color("#8296a5")); box.AddChild(tip);
    }

    private void StartBattle()
    {
        foreach (var soldier in _units) soldier.Node.QueueFree();
        _units.Clear(); _armies.Clear();
        foreach (var marker in _markersRoot.GetChildren()) marker.QueueFree();
        GenerateTerrain(); _elapsed = 0; _spawnClock = 0; _running = true;
        int count = _count.GetSelectedId();
        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.Tau * i / count - Mathf.Pi / 2;
            float radius = _rng.RandfRange(16.5f, 19.5f);
            var home = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            home.Y = HeightAt(home.X, home.Z);
            _armies.Add(new Army(i, Names[i], Colors[i], home)); AddMarker(home, Colors[i], Names[i]);
            for (int troop = 0; troop < 6; troop++) SpawnSoldier(_armies[i]);
        }
        _status.Text = "战斗进行中 · 各势力正在向战场中心推进";
    }

    private void AddMarker(Vector3 position, Color color, string text)
    {
        var mesh = new CylinderMesh { TopRadius = 1.35f, BottomRadius = 1.55f, Height = .1f };
        var material = new StandardMaterial3D { AlbedoColor = color.Darkened(.5f), EmissionEnabled = true, Emission = color.Darkened(.35f) };
        _markersRoot.AddChild(new MeshInstance3D { Mesh = mesh, Position = position + new Vector3(0, .06f, 0), MaterialOverride = material });
        _markersRoot.AddChild(new Label3D { Text = text, Position = position + new Vector3(0, .3f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, Modulate = color });
    }

    public override void _Process(double delta)
    {
        if (!_running) return;
        float dt = (float)delta; _elapsed += dt; _spawnClock += dt;
        if (_spawnClock >= .72f) { _spawnClock = 0; SpawnRound(); }
        Simulate(dt); UpdateScore();
    }

    private void SpawnRound()
    {
        foreach (var army in _armies)
        {
            if (army.UnitCount >= 34) continue;
            var direction = (new Vector3(-army.Home.X * .25f, 0, -army.Home.Z * .25f) - army.Home).Normalized();
            var side = new Vector3(-direction.Z, 0, direction.X);
            SpawnSoldier(army, side, direction);
        }
    }

    private void SpawnSoldier(Army army, Vector3 side = default, Vector3 direction = default)
    {
        if (side == Vector3.Zero)
        {
            direction = new Vector3(-army.Home.X, 0, -army.Home.Z).Normalized();
            side = new Vector3(-direction.Z, 0, direction.X);
        }
        var position = army.Home + side * _rng.RandfRange(-2.7f, 2.7f) + direction * _rng.RandfRange(-.5f, 1.3f);
        position.Y = HeightAt(position.X, position.Z);
        var soldier = MakeSoldier(army.Color); AddChild(soldier); soldier.Position = position;
        _units.Add(new Soldier(soldier, army.Id, _rng.RandfRange(1.8f, 2.5f), _rng.Randf() * Mathf.Tau)); army.UnitCount++;
    }

    private Node3D MakeSoldier(Color color)
    {
        var root = new Node3D(); var material = new StandardMaterial3D { AlbedoColor = color, Metallic = .22f, Roughness = .4f, EmissionEnabled = true, Emission = color.Darkened(.75f) };
        root.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = .27f, Height = .9f }, Position = new Vector3(0, .52f, 0), MaterialOverride = material });
        root.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .2f, Height = .4f }, Position = new Vector3(0, 1.08f, 0), MaterialOverride = material }); return root;
    }

    private void Simulate(float delta)
    {
        for (int i = 0; i < _units.Count; i++)
        {
            var unit = _units[i]; float nearestDistance = float.MaxValue; Soldier? nearest = null;
            foreach (var other in _units)
            {
                if (other.Faction == unit.Faction) continue;
                float distance = unit.Node.Position.DistanceTo(other.Node.Position);
                if (distance < nearestDistance) { nearestDistance = distance; nearest = other; }
            }
            if (nearest is not null && nearestDistance < 1.5f)
            {
                unit.Attack -= delta;
                if (unit.Attack <= 0)
                {
                    unit.Attack = _rng.RandfRange(.55f, .9f); nearest.Hp -= 1;
                    if (nearest.Hp <= 0) _armies[unit.Faction].Kills++;
                }
            }
            else if (nearest is not null)
            {
                var target = nearest.Node.Position;
                var travel = new Vector3(target.X - unit.Node.Position.X, 0, target.Z - unit.Node.Position.Z).Normalized();
                unit.Node.Position += travel * unit.Speed * delta;
                unit.Node.Position = new Vector3(unit.Node.Position.X, HeightAt(unit.Node.Position.X, unit.Node.Position.Z), unit.Node.Position.Z);
                unit.Node.LookAt(new Vector3(target.X, unit.Node.Position.Y, target.Z));
            }
            else
            {
                var towardCenter = new Vector3(-unit.Node.Position.X, 0, -unit.Node.Position.Z).Normalized();
                unit.Node.Position += towardCenter * unit.Speed * delta;
                unit.Node.Position = new Vector3(unit.Node.Position.X, HeightAt(unit.Node.Position.X, unit.Node.Position.Z), unit.Node.Position.Z);
            }
        }
        for (int i = _units.Count - 1; i >= 0; i--)
        {
            if (_units[i].Hp > 0) continue;
            _units[i].Node.QueueFree(); _units.RemoveAt(i);
        }
        foreach (var army in _armies) army.UnitCount = 0;
        foreach (var unit in _units) _armies[unit.Faction].UnitCount++;
        int alive = 0; foreach (var army in _armies) if (army.UnitCount > 0) alive++;
        if (alive <= 1) { _running = false; Army? winner = _armies.Find(a => a.UnitCount > 0); _status.Text = $"战斗结束 · {winner?.Name ?? "无人"} 获胜"; }
    }

    private void UpdateScore()
    {
        var lines = new List<string>(); foreach (var army in _armies) lines.Add($"[color=#{army.Color.ToHtml(false)}]{army.Name}[/color]  {army.UnitCount} 人  ·  击败 {army.Kills}");
        _score.Text = string.Join("\n", lines) + $"\n\n战场时间  {(int)_elapsed / 60:00}:{(int)_elapsed % 60:00}";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Space)
        { _running = !_running; if (_armies.Count > 0) _status.Text = _running ? "战斗进行中" : "战斗已暂停 · 按空格继续"; }
        if (@event is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Left) _dragging = mouse.Pressed;
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelUp) _cameraPivot.GetChild<Camera3D>(0).Position *= .9f;
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelDown) _cameraPivot.GetChild<Camera3D>(0).Position *= 1.1f;
        }
        if (@event is InputEventMouseMotion motion && _dragging) _cameraPivot.RotateY(-motion.Relative.X * .005f);
    }
}
