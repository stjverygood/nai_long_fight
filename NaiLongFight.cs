using Godot;
using System;
using System.Collections.Generic;

public partial class NaiLongFight : Node3D
{
    private static readonly Color[] Colors = [new("#ff5d73"), new("#55a7ff"), new("#ffc857"), new("#68e0a3"), new("#b889ff"), new("#ff914d"), new("#55d8d0"), new("#f47cc4")];
    private static readonly string[] Names = ["赤焰", "苍蓝", "金戈", "青岚", "紫霄", "烈阳", "碧海", "绯月"];
    private const float Arena = 68f;
    private readonly List<Army> _armies = [];
    private readonly List<Soldier> _units = [];
    private readonly List<Crystal> _crystals = [];
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

    private enum SoldierType { Infantry, Archer, Cavalry, Heavy }
    private enum Tactic { GatherAndCharge, WaveAttack, HoldAndCounter }
    private enum Strategy { CaptureCrystals, AttackRival, DefendCrystal, Regroup }

    private sealed class Army(int id, string name, Color color, Vector3 home, Tactic tactic, int rallyGoal)
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
        public Color Color { get; } = color;
        public Vector3 Home { get; } = home;
        public Tactic BattleTactic { get; } = tactic;
        public int RallyGoal { get; } = rallyGoal;
        public Strategy CurrentStrategy = Strategy.CaptureCrystals;
        public float DecisionClock;
        public int LastLaunchedCount;
        public int Kills;
        public int UnitCount;
        public bool Alive = true;
    }

    private sealed class Crystal(Node3D node, Vector3 position, int owner, bool initial)
    {
        public Node3D Node { get; } = node;
        public Vector3 Position { get; } = position;
        public int Owner = owner;
        public bool IsInitial { get; } = initial;
        public int CaptureFaction = -1;
        public float CaptureProgress;
        public float SpawnClock;
    }

    private sealed class Soldier(Node3D node, int faction, Crystal origin, SoldierType type, float speed, float damage, float range, bool deployed, Vector2 formation)
    {
        public Node3D Node { get; } = node;
        public int Faction { get; } = faction;
        public Crystal Origin { get; } = origin;
        public SoldierType Type { get; } = type;
        public float Speed { get; } = speed;
        public float Damage { get; } = damage;
        public float Range { get; } = range;
        public Vector2 Formation { get; } = formation;
        public bool Deployed = deployed;
        public float Hp = 4f;
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
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("#18232b"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color("#d8dfcf"), AmbientLightEnergy = .48f };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-52, -25, 0), LightEnergy = .92f, ShadowEnabled = true });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, 150, 0), LightEnergy = .16f, ShadowEnabled = false, LightColor = new Color("#afc7d8") });
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
        var normals = new Vector3[vertices.Length];
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
            const float normalStep = .15f;
            float slopeX = HeightAt(wx + normalStep, wz) - HeightAt(wx - normalStep, wz);
            float slopeZ = HeightAt(wx, wz + normalStep) - HeightAt(wx, wz - normalStep);
            normals[index] = new Vector3(-slopeX, normalStep * 2f, -slopeZ).Normalized();
            float shade = _terrainNoise.GetNoise2D(wx * 1.9f, wz * 1.9f) * .045f;
            Color baseColor = h > 2.0f ? new Color("#837c67") : h < -1.5f ? new Color("#48645f") : new Color("#60794f");
            colors[index] = new Color(Mathf.Clamp(baseColor.R + shade, 0, 1), Mathf.Clamp(baseColor.G + shade, 0, 1), Mathf.Clamp(baseColor.B + shade, 0, 1));
            if (x == resolution || z == resolution) continue;
            int a = index, b = index + resolution + 1, c = index + 1, d = b + 1;
            int t = (z * resolution + x) * 6;
            // Godot culls triangle backs using clockwise winding; reverse the grid order so the top faces are visible.
            indices[t] = a; indices[t + 1] = c; indices[t + 2] = b;
            indices[t + 3] = c; indices[t + 4] = d; indices[t + 5] = b;
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors; arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .96f });
        _terrainRoot.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    private void BuildCamera()
    {
        _cameraPivot = new Node3D(); AddChild(_cameraPivot);
        var camera = new Camera3D { Position = new Vector3(0, 145, 132), Fov = 56 };
        _cameraPivot.AddChild(camera); camera.LookAt(Vector3.Zero);
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer(); AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(24, 66), CustomMinimumSize = new Vector2(460, 0) };
        var style = new StyleBoxFlat { BgColor = new Color("#e8101822"), BorderColor = new Color("#405363"), ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 16, ContentMarginBottom = 16 };
        style.SetBorderWidthAll(1); style.SetCornerRadiusAll(12); panel.AddThemeStyleboxOverride("panel", style); layer.AddChild(panel);
        var toggle = new Button { Text = "收起面板  ▴", Position = new Vector2(24, 24), CustomMinimumSize = new Vector2(126, 34) };
        toggle.Pressed += () =>
        {
            panel.Visible = !panel.Visible;
            toggle.Text = panel.Visible ? "收起面板  ▴" : "展开面板  ▾";
        };
        layer.AddChild(toggle);
        var box = new VBoxContainer { Theme = null }; box.AddThemeConstantOverride("separation", 12); panel.AddChild(box);
        var title = new Label { Text = "LONG FIGHT  /  自动战场" }; title.AddThemeFontSizeOverride("font_size", 23); box.AddChild(title);
        var subtitle = new Label { Text = "势力自行扩军、遭遇与争夺战场" }; subtitle.AddThemeColorOverride("font_color", new Color("#9aafbe")); box.AddChild(subtitle);
        var row = new HBoxContainer(); box.AddChild(row);
        row.AddChild(new Label { Text = "参战势力", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _count = new OptionButton(); for (int n = 2; n <= 8; n++) _count.AddItem($"{n} 支", n); _count.Select(2); row.AddChild(_count);
        var start = new Button { Text = "开始自动战斗", CustomMinimumSize = new Vector2(0, 42) }; start.Pressed += StartBattle; box.AddChild(start);
        _status = new Label { Text = "选择势力数量，开始后战斗将持续进行。", AutowrapMode = TextServer.AutowrapMode.WordSmart }; _status.AddThemeColorOverride("font_color", new Color("#b3c2ce")); box.AddChild(_status);
        _score = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _score.AddThemeFontSizeOverride("font_size", 13); box.AddChild(_score);
        var tip = new Label { Text = "WASD 移动视角 · 拖动旋转 · 滚轮缩放 · 空格暂停" }; tip.AddThemeFontSizeOverride("font_size", 12); tip.AddThemeColorOverride("font_color", new Color("#8296a5")); box.AddChild(tip);
    }

    private void StartBattle()
    {
        foreach (var soldier in _units) soldier.Node.QueueFree();
        _units.Clear(); _armies.Clear(); _crystals.Clear();
        foreach (var marker in _markersRoot.GetChildren()) marker.QueueFree();
        GenerateTerrain(); _elapsed = 0; _spawnClock = 0; _running = true;
        int count = _count.GetSelectedId();
        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.Tau * i / count - Mathf.Pi / 2;
            float radius = _rng.RandfRange(48f, 56f);
            var home = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            home.Y = HeightAt(home.X, home.Z);
            var tactic = (Tactic)_rng.RandiRange(0, 2);
            int rallyGoal = tactic == Tactic.WaveAttack ? _rng.RandiRange(5, 7) : _rng.RandiRange(9, 12);
            _armies.Add(new Army(i, Names[i], Colors[i], home, tactic, rallyGoal));
            var homeCrystal = CreateCrystal(home, i, true);
            for (int troop = 0; troop < 6; troop++) SpawnSoldier(_armies[i], homeCrystal);
        }
        CreateNeutralCrystals(count * 2 + 4);
        _status.Text = "势力围绕初始水晶集结 · 占领更多水晶来扩军";
    }

    private Crystal CreateCrystal(Vector3 position, int owner, bool initial)
    {
        var root = new Node3D { Position = position };
        var baseMaterial = new StandardMaterial3D { AlbedoColor = new Color("#263341"), Metallic = .35f, Roughness = .4f };
        root.AddChild(new MeshInstance3D { Name = "Pedestal", Mesh = new CylinderMesh { TopRadius = .8f, BottomRadius = 1.1f, Height = .3f }, Position = new Vector3(0, .15f, 0), MaterialOverride = baseMaterial });
        var ownerColor = owner < 0 ? new Color("#d7e6ed") : Colors[owner];
        var gemMaterial = new StandardMaterial3D { AlbedoColor = ownerColor, EmissionEnabled = true, Emission = ownerColor * .32f, Metallic = .18f, Roughness = .22f };
        root.AddChild(new MeshInstance3D { Name = "Gem", Mesh = new SphereMesh { Radius = .72f, Height = 1.85f, RadialSegments = 6, Rings = 3 }, Position = new Vector3(0, 1.15f, 0), MaterialOverride = gemMaterial });
        root.AddChild(new Label3D { Name = "Label", Text = initial ? $"{Names[owner]}\n初始水晶" : "中立水晶", Position = new Vector3(0, 2.25f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 30, Modulate = ownerColor });
        _markersRoot.AddChild(root);
        var crystal = new Crystal(root, position, owner, initial); _crystals.Add(crystal); return crystal;
    }

    private void CreateNeutralCrystals(int amount)
    {
        var placed = new List<Vector3>();
        foreach (var army in _armies) placed.Add(army.Home);
        for (int i = 0; i < amount; i++)
        {
            bool created = false;
            for (int attempt = 0; attempt < 80 && !created; attempt++)
            {
                float x = _rng.RandfRange(-Arena * .72f, Arena * .72f);
                float z = _rng.RandfRange(-Arena * .72f, Arena * .72f);
                var p = new Vector3(x, HeightAt(x, z), z);
                bool clear = true;
                foreach (var other in placed) if (p.DistanceTo(other) < 18f) { clear = false; break; }
                if (!clear) continue;
                CreateCrystal(p, -1, false); placed.Add(p); created = true;
            }
        }
    }

    public override void _Process(double delta)
    {
        MoveCamera((float)delta);
        if (!_running) return;
        float dt = (float)delta; _elapsed += dt; _spawnClock += dt;
        if (_spawnClock >= .72f) { _spawnClock = 0; SpawnRound(); }
        ProcessCrystals(dt); DecideStrategies(dt); Simulate(dt); CheckEliminations(); UpdateScore();
    }

    private void MoveCamera(float delta)
    {
        var input = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A)) input.X -= 1;
        if (Input.IsKeyPressed(Key.D)) input.X += 1;
        if (Input.IsKeyPressed(Key.W)) input.Y += 1;
        if (Input.IsKeyPressed(Key.S)) input.Y -= 1;
        if (input == Vector2.Zero) return;
        input = input.Normalized();
        var forward = -_cameraPivot.GlobalBasis.Z; forward.Y = 0; forward = forward.Normalized();
        var right = _cameraPivot.GlobalBasis.X; right.Y = 0; right = right.Normalized();
        _cameraPivot.Position += (right * input.X + forward * input.Y) * 28f * delta;
        _cameraPivot.Position = new Vector3(Mathf.Clamp(_cameraPivot.Position.X, -Arena * .8f, Arena * .8f), 0, Mathf.Clamp(_cameraPivot.Position.Z, -Arena * .8f, Arena * .8f));
    }

    private void SpawnRound()
    {
        foreach (var army in _armies)
        {
            if (!army.Alive || army.UnitCount >= 48) continue;
            foreach (var crystal in _crystals)
            {
                if (crystal.Owner != army.Id || army.UnitCount >= 48) continue;
                crystal.SpawnClock += .72f;
                if (crystal.SpawnClock < 1.44f) continue;
                crystal.SpawnClock = 0;
                SpawnSoldier(army, crystal);
            }
            int waitingCount = 0;
            foreach (var unit in _units) if (unit.Faction == army.Id && !unit.Deployed) waitingCount++;
            if (waitingCount >= army.RallyGoal) ReleaseGroup(army);
        }
    }

    private void ReleaseGroup(Army army)
    {
        int defendersToKeep = army.BattleTactic == Tactic.HoldAndCounter ? 2 : 0;
        int available = 0;
        foreach (var soldier in _units) if (soldier.Faction == army.Id && !soldier.Deployed) available++;
        int toRelease = Mathf.Max(0, available - defendersToKeep);
        if (toRelease == 0) return;
        foreach (var soldier in _units)
        {
            if (soldier.Faction != army.Id || soldier.Deployed) continue;
            soldier.Deployed = true;
            toRelease--;
            if (toRelease <= 0) break;
        }
        army.LastLaunchedCount += available - defendersToKeep;
    }

    private void SpawnSoldier(Army army, Crystal source)
    {
        int slot = 0;
        foreach (var soldier in _units) if (ReferenceEquals(soldier.Origin, source)) slot++;
        int column = slot % 4;
        int row = slot / 4;
        var formation = new Vector2((column - 1.5f) * 1.05f, row * 1.0f);
        var towardCenter = new Vector3(-source.Position.X, 0, -source.Position.Z).Normalized();
        var lateral = new Vector3(-towardCenter.Z, 0, towardCenter.X);
        var position = source.Position + lateral * formation.X - towardCenter * formation.Y;
        position.Y = HeightAt(position.X, position.Z);
        var type = (SoldierType)_rng.RandiRange(0, 3);
        var soldierNode = MakeSoldier(army.Color, type); AddChild(soldierNode); soldierNode.Position = position;
        float speed = type switch { SoldierType.Cavalry => 3.4f, SoldierType.Heavy => 1.45f, SoldierType.Archer => 1.85f, _ => 2.25f };
        float damage = type switch { SoldierType.Heavy => 1.8f, SoldierType.Archer => 1.25f, _ => 1f };
        float range = type switch { SoldierType.Archer => 5.5f, SoldierType.Cavalry => 1.55f, SoldierType.Heavy => 1.35f, _ => 1.45f };
        bool deployed = false;
        _units.Add(new Soldier(soldierNode, army.Id, source, type, speed, damage, range, deployed, formation)); army.UnitCount++;
    }

    private void ProcessCrystals(float delta)
    {
        foreach (var crystal in _crystals)
        {
            var pressure = new int[_armies.Count];
            foreach (var soldier in _units)
            {
                if (soldier.Deployed && soldier.Hp > 0 && PlanarDistance(soldier.Node.Position, crystal.Position) < 4.6f)
                    pressure[soldier.Faction]++;
            }
            int contender = -1, maximum = 0; bool tie = false;
            for (int i = 0; i < pressure.Length; i++)
            {
                if (pressure[i] > maximum) { maximum = pressure[i]; contender = i; tie = false; }
                else if (pressure[i] > 0 && pressure[i] == maximum) tie = true;
            }
            if (maximum == 0 || tie || contender == crystal.Owner)
            {
                crystal.CaptureFaction = -1; crystal.CaptureProgress = Mathf.Max(0, crystal.CaptureProgress - delta * .5f);
                continue;
            }
            if (crystal.CaptureFaction != contender) { crystal.CaptureFaction = contender; crystal.CaptureProgress = 0; }
            crystal.CaptureProgress += delta;
            if (crystal.CaptureProgress < 4f) continue;
            int oldOwner = crystal.Owner; crystal.Owner = contender; crystal.CaptureFaction = -1; crystal.CaptureProgress = 0;
            RefreshCrystal(crystal);
            _status.Text = $"{_armies[contender].Name} 占领了水晶！水晶开始为其出兵";
            if (oldOwner >= 0) _armies[oldOwner].DecisionClock = 99;
            _armies[contender].DecisionClock = 99;
        }
    }

    private void RefreshCrystal(Crystal crystal)
    {
        var color = crystal.Owner < 0 ? new Color("#d7e6ed") : Colors[crystal.Owner];
        var gem = crystal.Node.GetNode<MeshInstance3D>("Gem");
        var material = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color * .2f, Metallic = .18f, Roughness = .22f };
        gem.MaterialOverride = material;
        var label = crystal.Node.GetNode<Label3D>("Label");
        label.Text = crystal.Owner < 0 ? "中立水晶" : $"{Names[crystal.Owner]}水晶";
        label.Modulate = color;
    }

    private void DecideStrategies(float delta)
    {
        foreach (var army in _armies)
        {
            if (!army.Alive) continue;
            army.DecisionClock += delta;
            if (army.DecisionClock < 5f) continue;
            army.DecisionClock = 0;
            int owned = 0, available = 0;
            foreach (var crystal in _crystals) { if (crystal.Owner == army.Id) owned++; else available++; }
            bool threatened = false;
            foreach (var crystal in _crystals)
            {
                if (crystal.Owner != army.Id) continue;
                foreach (var soldier in _units)
                {
                    if (soldier.Faction != army.Id && PlanarDistance(soldier.Node.Position, crystal.Position) < 10f) { threatened = true; break; }
                }
                if (threatened) break;
            }
            float rivalAverage = 0; int rivals = 0;
            foreach (var rival in _armies) if (rival.Alive && rival.Id != army.Id) { rivalAverage += rival.UnitCount; rivals++; }
            if (rivals > 0) rivalAverage /= rivals;
            if (threatened) army.CurrentStrategy = Strategy.DefendCrystal;
            else if (rivalAverage > 0 && army.UnitCount < rivalAverage * .65f && owned > 0) army.CurrentStrategy = Strategy.Regroup;
            else if (available > 0 && _rng.Randf() < (owned <= 1 ? .9f : .68f)) army.CurrentStrategy = Strategy.CaptureCrystals;
            else army.CurrentStrategy = _rng.Randf() < .62f ? Strategy.AttackRival : Strategy.Regroup;
        }
    }

    private void CheckEliminations()
    {
        foreach (var army in _armies)
        {
            if (!army.Alive) continue;
            bool ownsCrystal = false;
            foreach (var crystal in _crystals) if (crystal.Owner == army.Id) { ownsCrystal = true; break; }
            if (ownsCrystal) continue;
            army.Alive = false;
            for (int i = _units.Count - 1; i >= 0; i--)
            {
                if (_units[i].Faction != army.Id) continue;
                _units[i].Node.QueueFree(); _units.RemoveAt(i);
            }
        }
        int alive = 0; Army? winner = null;
        foreach (var army in _armies) if (army.Alive) { alive++; winner = army; }
        if (alive <= 1)
        {
            _running = false;
            _status.Text = alive == 1 ? $"战斗结束 · {winner!.Name} 占领了全部水晶" : "所有势力均已覆灭";
        }
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private Node3D MakeSoldier(Color color, SoldierType type)
    {
        var root = new Node3D();
        var material = new StandardMaterial3D { AlbedoColor = color, Metallic = .18f, Roughness = .48f, EmissionEnabled = true, Emission = color.Darkened(.72f) };
        float radius = type switch { SoldierType.Heavy => .38f, SoldierType.Cavalry => .32f, SoldierType.Archer => .22f, _ => .27f };
        float height = type switch { SoldierType.Heavy => 1.05f, SoldierType.Archer => .78f, _ => .9f };
        root.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = radius, Height = height }, Position = new Vector3(0, height * .56f, 0), MaterialOverride = material });
        root.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = radius * .72f, Height = radius * 1.44f }, Position = new Vector3(0, height + .05f, 0), MaterialOverride = material });
        if (type == SoldierType.Cavalry)
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.34f, .3f, .62f) }, Position = new Vector3(0, .38f, .18f), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#5c382e"), Roughness = .8f } });
        if (type == SoldierType.Archer)
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.07f, .65f, .07f) }, Position = new Vector3(.3f, .65f, 0), RotationDegrees = new Vector3(0, 0, -18), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#d8c28b") } });
        if (type == SoldierType.Heavy)
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.12f, .72f, .12f) }, Position = new Vector3(.37f, .62f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#b6c1c7"), Metallic = .45f } });
        return root;
    }

    private void Simulate(float delta)
    {
        for (int i = 0; i < _units.Count; i++)
        {
            var unit = _units[i];
            if (!unit.Deployed) continue;
            float nearestDistance = float.MaxValue; Soldier? nearest = null;
            var separation = Vector3.Zero;
            foreach (var other in _units)
            {
                if (ReferenceEquals(other, unit)) continue;
                var offset = unit.Node.Position - other.Node.Position;
                offset.Y = 0;
                float distance = offset.Length();
                if (other.Faction == unit.Faction)
                {
                    if (distance > .001f && distance < 1.35f) separation += offset.Normalized() * (1.35f - distance);
                    continue;
                }
                if (other.Hp > 0 && distance < nearestDistance) { nearestDistance = distance; nearest = other; }
            }
            if (nearest is not null && nearestDistance <= unit.Range)
            {
                unit.Attack -= delta;
                if (unit.Attack <= 0)
                {
                    unit.Attack = unit.Type == SoldierType.Archer ? _rng.RandfRange(.9f, 1.2f) : _rng.RandfRange(.55f, .9f);
                    nearest.Hp -= unit.Damage;
                    if (nearest.Hp <= 0) _armies[unit.Faction].Kills++;
                }
                if (separation.LengthSquared() > .001f)
                    unit.Node.Position += separation.Normalized() * delta * 1.5f;
            }
            else
            {
                Vector3 target;
                if (nearest is not null && nearestDistance < 7f && _armies[unit.Faction].CurrentStrategy is Strategy.AttackRival or Strategy.DefendCrystal)
                    target = nearest.Node.Position;
                else
                {
                    var objective = GetStrategicTarget(_armies[unit.Faction], unit);
                    target = objective ?? nearest?.Node.Position ?? unit.Node.Position;
                    if (objective is not null)
                    {
                        var radial = new Vector3(target.X, 0, target.Z).Normalized();
                        var flank = new Vector3(-radial.Z, 0, radial.X);
                        target += flank * unit.Formation.X * .55f - radial * Mathf.Min(unit.Formation.Y, 3f) * .5f;
                    }
                }
                var travel = new Vector3(target.X - unit.Node.Position.X, 0, target.Z - unit.Node.Position.Z).Normalized();
                var steering = travel + separation * 1.3f;
                if (steering.LengthSquared() > .001f) unit.Node.Position += steering.Normalized() * unit.Speed * delta;
                unit.Node.Position = new Vector3(unit.Node.Position.X, HeightAt(unit.Node.Position.X, unit.Node.Position.Z), unit.Node.Position.Z);
                unit.Node.LookAt(new Vector3(target.X, unit.Node.Position.Y, target.Z));
            }
        }
        for (int i = _units.Count - 1; i >= 0; i--)
        {
            if (_units[i].Hp > 0) continue;
            _units[i].Node.QueueFree(); _units.RemoveAt(i);
        }
        foreach (var army in _armies) army.UnitCount = 0;
        foreach (var unit in _units) _armies[unit.Faction].UnitCount++;
    }

    private Vector3? GetStrategicTarget(Army army, Soldier unit)
    {
        Crystal? best = null; float bestDistance = float.MaxValue;
        foreach (var crystal in _crystals)
        {
            bool desired = army.CurrentStrategy switch
            {
                Strategy.CaptureCrystals => crystal.Owner != army.Id,
                Strategy.AttackRival => crystal.Owner >= 0 && crystal.Owner != army.Id,
                Strategy.DefendCrystal or Strategy.Regroup => crystal.Owner == army.Id,
                _ => false
            };
            if (!desired) continue;
            float distance = PlanarDistance(unit.Node.Position, crystal.Position);
            if (distance < bestDistance) { best = crystal; bestDistance = distance; }
        }
        if (best is not null) return best.Position;
        if (army.CurrentStrategy != Strategy.AttackRival)
        {
            foreach (var crystal in _crystals)
            {
                if (crystal.Owner != army.Id) continue;
                float distance = PlanarDistance(unit.Node.Position, crystal.Position);
                if (distance < bestDistance) { best = crystal; bestDistance = distance; }
            }
        }
        return best?.Position;
    }

    private void UpdateScore()
    {
        var lines = new List<string>();
        foreach (var army in _armies)
        {
            int infantry = 0, archers = 0, cavalry = 0, heavy = 0;
            foreach (var soldier in _units)
            {
                if (soldier.Faction != army.Id) continue;
                switch (soldier.Type)
                {
                    case SoldierType.Infantry: infantry++; break;
                    case SoldierType.Archer: archers++; break;
                    case SoldierType.Cavalry: cavalry++; break;
                    case SoldierType.Heavy: heavy++; break;
                }
            }
            int ownedCrystals = 0;
            foreach (var crystal in _crystals) if (crystal.Owner == army.Id) ownedCrystals++;
            string strategy = army.CurrentStrategy switch
            {
                Strategy.CaptureCrystals => "争夺水晶",
                Strategy.AttackRival => "攻击敌军",
                Strategy.DefendCrystal => "防守水晶",
                _ => "回防集结"
            };
            string state = army.Alive ? strategy : "已覆灭";
            lines.Add($"{army.Name}  {state} · 水晶{ownedCrystals} · 兵{army.UnitCount}（步{infantry}/弓{archers}/骑{cavalry}/重{heavy}）· 击败{army.Kills}");
        }
        int neutral = 0; foreach (var crystal in _crystals) if (crystal.Owner < 0) neutral++;
        _score.Text = string.Join("\n", lines) + $"\n\n中立水晶 {neutral}  ·  战场时间  {(int)_elapsed / 60:00}:{(int)_elapsed % 60:00}";
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
