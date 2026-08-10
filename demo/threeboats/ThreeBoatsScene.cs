using Crest.Godot;
using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

/// Runtime reconstruction of Crest's BoatDev/threeboats example.
public partial class ThreeBoatsScene : Node3D
{
    private readonly Color _boatGray = new(0.5f, 0.5f, 0.5f);
    private ThreeBoatsFlyCamera? _camera;
    private CrestOceanRendererFacade? _ocean;
    private RigidBody3D? _liner;
    private Label? _label;
    private int _shotFrame = -1;
    private int _frame;
    private bool _wakeCloseup;
    private bool _wakeOverhead;
    private float _wakeOverheadHeight = 100.0f;
    private bool _lodReplay;
    private int _lodReplayFrame;
    private string _lodReplayDirectory = "";

    private static readonly Vector3 LodReplayStart = new(-57.385f, 32.457f, 10.075f);
    private static readonly Vector3 LodReplayEnd = new(-64.869f, 41.402f, 7.249f);
    private static readonly Vector3 LodReplayRotation = new(-48.189f, -110.683f, 0.0f);
    private const double LodReplayStartTime = 20.5;
    private const double LodReplayDuration = 4.0;

    public override void _Ready()
    {
        GD.Print($"THREEBOATS_PHYSICS_ENGINE {ProjectSettings.GetSetting("physics/3d/physics_engine")}");
        ConfigureReferenceOcean();

        var objects = new Node3D { Name = "Objects" };
        AddChild(objects);
        CreateAlignBoat(objects, "BoatAlignNormal1", new Vector3(-6.2f, 5.0f, 24.9f), 1.0f, 1.0f);
        CreateAlignBoat(objects, "BoatAlignNormal2", new Vector3(67.1f, 5.0f, 11.5f), 1.0f, -0.8f);
        CreateAlignBoat(objects, "BoatAlignNormal3", new Vector3(25.7f, 5.0f, 8.7f), 1.0f, -0.7f);
        CreateMediumBoat(objects);
        _liner = CreateOceanLiner(objects);
        CreateCamera();
        CreateHud();
        ParseArguments();
    }

    public override void _Process(double delta)
    {
        _ = delta;

        if ((_wakeCloseup || _wakeOverhead) && _camera != null &&
            GetNodeOrNull<RigidBody3D>("Objects/BoatAlignNormal3") is { } boat)
        {
            _camera.GlobalPosition = boat.GlobalPosition +
                (_wakeOverhead ? new Vector3(0, _wakeOverheadHeight, 0.01f) : new Vector3(-12, 7, -12));
            _camera.LookAt(boat.GlobalPosition);
            _camera.SyncLookAngles();
        }

        if (_lodReplay && _camera != null && _ocean != null)
        {
            var replayTime = _ocean.CurrentTime;
            var alpha = Mathf.Clamp((float)((replayTime - LodReplayStartTime) / LodReplayDuration), 0.0f, 1.0f);
            _camera.GlobalPosition = LodReplayStart.Lerp(LodReplayEnd, alpha);
            _camera.GlobalRotationDegrees = LodReplayRotation;
            _camera.SyncLookAngles();

            if (replayTime >= LodReplayStartTime)
            {
                var finalFrame = replayTime >= LodReplayStartTime + LodReplayDuration;
                var captureIndex = _lodReplayFrame++;
                CallDeferred(MethodName.CaptureLodReplayFrame, captureIndex, replayTime,
                    _ocean.GetOceanScale(), _ocean.GetViewerScaleTransitionBlend(), finalFrame);
                if (finalFrame) _lodReplay = false;
            }
        }

        if (_label != null)
        {
            var position = _camera?.GlobalPosition ?? Vector3.Zero;
            var rotation = _camera?.GlobalRotationDegrees ?? Vector3.Zero;
            var simulationTime = _ocean?.CurrentTime ?? 0.0;
            var oceanScale = _ocean?.GetOceanScale() ?? 0.0f;
            var scaleBlend = _ocean?.GetViewerScaleTransitionBlend() ?? 0.0f;
            _label.Text = "CREST — THREE BOATS\n" +
                "Click + mouse: look  |  WASD/QE: fly  |  Shift: fast  |  Esc: release mouse\n" +
                $"Camera XYZ: {position.X:F3}, {position.Y:F3}, {position.Z:F3}\n" +
                $"Rotation XYZ: {rotation.X:F3}, {rotation.Y:F3}, {rotation.Z:F3} deg\n" +
                $"Simulation time: {simulationTime:F3} s  |  Ocean LOD: {oceanScale:F0}  blend: {scaleBlend:F3}";
        }

        _frame++;
        if (_frame == _shotFrame)
            CallDeferred(MethodName.CaptureScreenshot);
    }

    private void ConfigureReferenceOcean()
    {
        var ocean = GetNodeOrNull<CrestOceanRendererFacade>("Main/CrestOceanRenderer");
        if (ocean == null) return;
        _ocean = ocean;
        ocean._minScale = 8.0f;
        ocean._maxScale = 256.0f;
        ocean._lodDataResolution = 384;
        ocean._geometryDownSampleFactor = 2;
        ocean._createFoamSim = true;
        ocean._createDynamicWaveSim = true;
        ocean._simSettingsFoam = new CrestSimSettingsFoam
        {
            _prewarm = true,
            _foamFadeRate = 0.1f,
            _waveFoamStrength = 3.27f,
            _waveFoamCoverage = 0.569f,
            _shorelineFoamMaxDepth = 0.65f,
            _shorelineFoamStrength = 2.0f,
            _simulationFrequency = 30.0f,
        };
        if (ocean._simSettingsDynamicWaves is { } dynamicWaves)
        {
            dynamicWaves._simulationFrequency = 120.0f;
            dynamicWaves._damping = 0.0f;
            dynamicWaves._courantNumber = 1.0f;
            dynamicWaves._attenuationInShallows = 1.0f;
            dynamicWaves._horizDisplace = 9.66f;
            dynamicWaves._displaceClamp = 0.3f;
            dynamicWaves._gravityMultiplier = 1.0f;
        }

        var shape = ocean.GetNodeOrNull<CrestShapeGerstner>("CrestShapeGerstner");
        if (shape?._spectrum == null) return;
        shape._waveDirectionHeadingAngle = 180.0f;
        shape._windSpeed = 20.0f;
        shape._randomSeed = 1337;
        shape._spectrum._multiplier = 1.0f;
        shape._spectrum._chop = 1.69f;
        shape._spectrum._powerLog = new[] { -7.39794f, -7.39794f, -6.598513f, -6.1093907f,
            -5.6443586f, -5.0608916f, -4.513266f, -3.4068804f, -2.7731915f,
            -2.4056463f, -2.5869184f, -4.421911f, -7.39794f, -7.39794f };
        shape._spectrum.EmitChanged();
    }

    private void CreateAlignBoat(Node3D parent, string name, Vector3 position,
        float throttle, float steer)
    {
        var body = MakeBody(parent, name, position, 32000.0f, new Vector3(4, 1, 8));
        body.Rotation = new Vector3(0, Mathf.DegToRad(126.61f), 0);
        body.AngularDamp = 1.5f;

        AddMesh(body, "Hull", new CapsuleMesh { Radius = 0.5f, Height = 2.0f },
            Vector3.Zero, new Vector3(4.5f, 4.0f, 2.0f), new Vector3(Mathf.Pi / 2, 0, 0));
        AddMesh(body, "Cabin", new BoxMesh { Size = Vector3.One },
            new Vector3(0, 1.81f, -1.11f), new Vector3(2.3208153f, 3.3449237f, 3.3449237f));

        body.AddChild(new CrestSimpleFloatingObject {
            Name = "BoatAlignNormal", _raiseObject = 1.3f, _buoyancyCoeff = 1.5f,
            _boyancyTorque = 8.0f, _objectWidth = 4.5f, _forceHeightOffset = -0.3f,
            _dragInWaterUp = 3.0f, _dragInWaterRight = 2.0f, _dragInWaterForward = 1.0f,
        });
        body.AddChild(new ThreeBoatsMotor { Name = "Motor", EnginePower = 11.0f,
            TurnPower = 1.3f, ThrottleBias = throttle, SteerBias = steer });
        AddWake(body, new Vector3(0, 0, -1.94f), 2.4f, 3.0f, 0.1f, 0.04f, 0.75f);
        AddWake(body, new Vector3(0, 0, 2.39f), 2.4f, 2.0f, 0.1f, 0.04f, 0.75f);
    }

    private void CreateMediumBoat(Node3D parent)
    {
        var body = MakeBody(parent, "MediumBoat", new Vector3(22.6f, 0, -50.5f),
            5000.0f, new Vector3(6, 3.5f, 15));
        body.AngularDamp = 2.0f;
        AddMesh(body, "MainHull", new CapsuleMesh { Radius = 0.5f, Height = 2 }, Vector3.Zero,
            new Vector3(6, 8, 3.5649018f), new Vector3(Mathf.Pi / 2, 0, 0));
        AddMesh(body, "Bow", new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 2 },
            new Vector3(0.02f, 0.9f, 4.16f), new Vector3(6, 0.75608367f, 8.077367f));
        AddMesh(body, "Stern", new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 2 },
            new Vector3(0, 0, -5.5f), new Vector3(6, 2.5f, 3.3227222f), new Vector3(Mathf.Pi / 2, 0, 0));
        AddMesh(body, "Deck", new BoxMesh { Size = Vector3.One }, new Vector3(0, 0.9f, -2),
            new Vector3(6, 1.5f, 12));
        AddMesh(body, "Cabin", new BoxMesh { Size = Vector3.One }, new Vector3(0, 1.62f, -3.13f),
            new Vector3(3.4809f, 2.6801f, 7.0875673f));

        var probes = new List<Vector3>();
        foreach (var z in new[] { 6.0f, 2.0f, -2.0f, -6.0f })
            foreach (var x in new[] { -2.5f, 0.0f, 2.5f }) probes.Add(new Vector3(x, 0, z));
        probes[8] = new Vector3(2.0f, 0, -2.0f); // Upstream prefab's asymmetric point.
        AddProbeBuoyancy(body, probes, new Vector3(0, -1, 0), 6.0f, 3.0f, 4.0f, 0.5f);
        body.AddChild(new ThreeBoatsMotor { Name = "Motor", EnginePower = 5.0f,
            TurnPower = 0.5f, ThrottleBias = 1.0f, SteerBias = 1.0f });
        AddWake(body, new Vector3(0, 0, -5.9f), 2.6f, 3.0f, 0.5f, 0.04f, 1.0f);
        AddWake(body, new Vector3(0, 0, 5.44f), 3.3f, 5.0f, 0.5f, 0.07f, 0.45f);
    }

    private RigidBody3D CreateOceanLiner(Node3D parent)
    {
        var body = MakeBody(parent, "OceanLiner", new Vector3(320, 0, -317),
            50000000.0f, new Vector3(28, 34, 270));
        body.AngularDamp = 2.0f;
        body.LinearDamp = 0.2f;
        AddMesh(body, "Hull", new CapsuleMesh { Radius = 0.5f, Height = 2 }, Vector3.Zero,
            new Vector3(28.030594f, 134.80013f, 33.933342f), new Vector3(Mathf.Pi / 2, 0, 0));
        AddMesh(body, "LongHull", new BoxMesh { Size = Vector3.One }, new Vector3(0, 8.566855f, -33.7f),
            new Vector3(28.030594f, 14.278117f, 202.20026f));
        AddMesh(body, "Bow", new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 2 },
            new Vector3(0, 8.566847f, 70.096f), new Vector3(28.080664f, 7.1969504f, 136.03627f));
        AddMesh(body, "Superstructure", new BoxMesh { Size = Vector3.One },
            new Vector3(0, 15.420329f, -8.425f), new Vector3(16.26195f, 17.688906f, 119.425514f));
        AddMesh(body, "Stern", new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 2 },
            new Vector3(0, 0, -92.675f), new Vector3(28.030594f, 42.12505f, 31.628101f),
            new Vector3(Mathf.Pi / 2, 0, 0));

        var probes = new List<Vector3>();
        foreach (var z in new[] { 120.0f, 80.0f, 40.0f, 0.0f })
            foreach (var x in new[] { -11.0f, 0.0f, 11.0f }) probes.Add(new Vector3(x, 0, z));
        // Preserve the unusual asymmetric ordering/point from Crest's prefab.
        probes.Add(new Vector3(11, 0, -120));
        probes.Add(new Vector3(0, 0, -40));
        probes.Add(new Vector3(11, 0, -40));
        foreach (var z in new[] { -80.0f, -120.0f })
            foreach (var x in new[] { -11.0f, 0.0f, 11.0f }) probes.Add(new Vector3(x, 0, z));
        AddProbeBuoyancy(body, probes, new Vector3(0, -8, 0), 15000.0f, 1.0f, 4.0f, 0.0f);
        body.AddChild(new ThreeBoatsMotor { Name = "Motor", EnginePower = 4.0f,
            TurnPower = 0.15f, ThrottleBias = 1.0f, SteerBias = -0.4f });
        AddWake(body, new Vector3(0, 0, 130.41f), 5.0f, 4.0f);
        foreach (var z in new[] { 112.2f, 60.4f, 16.5f, -31.4f, -82.6f, -126.6f })
            AddWake(body, new Vector3(0, 0, z), 10.0f, 4.0f);
        return body;
    }

    private RigidBody3D MakeBody(Node3D parent, string name, Vector3 position, float mass, Vector3 collisionSize)
    {
        var body = new RigidBody3D { Name = name, Position = position, Mass = mass,
            ContinuousCd = true, CanSleep = false };
        parent.AddChild(body);
        var collision = new CollisionShape3D { Name = "HullCollision",
            Shape = new BoxShape3D { Size = collisionSize } };
        body.AddChild(collision);
        return body;
    }

    private void AddProbeBuoyancy(RigidBody3D body, List<Vector3> positions, Vector3 centerOfMass,
        float multiplier, float upDrag, float rightDrag, float forwardDrag)
    {
        var group = new Node3D { Name = "ForcePoints" };
        body.AddChild(group);
        var resources = new Array<CrestFloaterForcePoint>();
        for (var i = 0; i < positions.Count; i++)
        {
            var probe = new Marker3D { Name = $"P{i:00}", Position = positions[i] };
            group.AddChild(probe);
            resources.Add(new CrestFloaterForcePoint { _transform = $"../ForcePoints/{probe.Name}" });
        }
        var buoyancy = new CrestBoatProbes { Name = "BoatProbes", _forcePoints = resources,
            _centerOfMass = centerOfMass, _forceMultiplier = multiplier,
            _dragInWaterUp = upDrag, _dragInWaterRight = rightDrag,
            _dragInWaterForward = forwardDrag };
        body.AddChild(buoyancy);
    }

    private void AddWake(Node3D body, Vector3 position, float radius, float weight,
        float upDownMultiplier = 0.5f, float velocityOffset = 0.04f,
        float compensateForWaves = 0.45f)
    {
        // Compute injection is more heavily filtered than Crest's raster input
        // on the reference Unity renderer. Compensate so the wake has the same
        // readable amplitude at the Three Boats camera distance.
        const float godotInteractionGain = 4.0f;
        var wake = new CrestSphereWaterInteraction { Name = "InteractionSphere", Position = position,
            _radius = radius, _weight = weight * godotInteractionGain, _weightUpDownMul = upDownMultiplier,
            _velocityOffset = velocityOffset, _compensateForWaveMotion = compensateForWaves,
            _teleportSpeed = 500.0f, _maxSpeed = 100.0f, _warmUpDuration = 3.0f,
            _boostLargeWaves = radius <= 3.3f };
        wake.SetFoamStrength(0.7f);
        body.AddChild(wake);
    }

    private void AddMesh(Node3D parent, string name, PrimitiveMesh mesh, Vector3 position,
        Vector3 scale, Vector3? rotation = null)
    {
        var instance = new MeshInstance3D { Name = name, Mesh = mesh, Position = position,
            Scale = scale, Rotation = rotation ?? Vector3.Zero, MaterialOverride = GrayMaterial() };
        parent.AddChild(instance);
    }

    private StandardMaterial3D GrayMaterial() => new() { AlbedoColor = _boatGray, Roughness = 0.5f };

    private void CreateCamera()
    {
        GetNodeOrNull<Camera3D>("Main/Camera3D")?.SetCurrent(false);
        _camera = new ThreeBoatsFlyCamera { Name = "DemoCamera", Position = new Vector3(-37.1f, 9.94f, 1),
            Current = true, Far = 200000.0f, Fov = 60.0f };
        AddChild(_camera);
        // Unity quaternion (0.0511765, 0.6742806, -0.046935763, 0.73520315)
        // looks almost due +X and directly at BoatAlignNormal (3). LerpCam is
        // disabled in DemoCameraCore, so this is intentionally a static shot.
        _camera.LookAt(_camera.Position + new Vector3(0.986f, -0.138f, 0.085f));
        _camera.SyncLookAngles();
        _camera.AddChild(new CrestUnderwaterRenderer { Name = "CrestUnderwaterRenderer" });
    }

    private void CreateHud()
    {
        if (GetNodeOrNull<CanvasLayer>("Main/CanvasLayer") is { } oldHud) oldHud.Visible = false;
        var canvas = new CanvasLayer { Name = "ThreeBoatsHUD" };
        AddChild(canvas);
        _label = new Label { Name = "Title", Position = new Vector2(18, 16) };
        _label.AddThemeColorOverride("font_color", Colors.White);
        _label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        _label.AddThemeConstantOverride("shadow_offset_x", 2);
        _label.AddThemeConstantOverride("shadow_offset_y", 2);
        canvas.AddChild(_label);
    }

    private void ParseArguments()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shot-frame=") && int.TryParse(arg.Split('=', 2)[1], out var frame))
                _shotFrame = frame;
            else if (arg == "--wake-closeup")
                _wakeCloseup = true;
            else if (arg == "--wake-overhead")
                _wakeOverhead = true;
            else if (arg.StartsWith("--wake-overhead=") &&
                float.TryParse(arg.Split('=', 2)[1], out var overheadHeight))
            {
                _wakeOverhead = true;
                _wakeOverheadHeight = Mathf.Max(overheadHeight, 10.0f);
            }
            else if (arg.StartsWith("--cam=") && TryParseVector3(arg.Split('=', 2)[1], out var position))
                _camera!.GlobalPosition = position;
            else if (arg.StartsWith("--rot=") && TryParseVector3(arg.Split('=', 2)[1], out var rotation))
            {
                _camera!.GlobalRotationDegrees = rotation;
                _camera.SyncLookAngles();
            }
            else if (arg == "--lod-replay")
            {
                _lodReplay = true;
                _camera!.NavigationEnabled = false;
                _camera!.GlobalPosition = LodReplayStart;
                _camera.GlobalRotationDegrees = LodReplayRotation;
                _camera.SyncLookAngles();
                _lodReplayDirectory = $"user://lod_replay_{Godot.Time.GetTicksMsec()}";
                DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(_lodReplayDirectory));
                GD.Print($"THREEBOATS_LOD_REPLAY_DIR {ProjectSettings.GlobalizePath(_lodReplayDirectory)}");
            }
        }
    }

    private static bool TryParseVector3(string value, out Vector3 result)
    {
        var parts = value.Split(',', 3);
        if (parts.Length == 3 && float.TryParse(parts[0], out var x) &&
            float.TryParse(parts[1], out var y) && float.TryParse(parts[2], out var z))
        {
            result = new Vector3(x, y, z);
            return true;
        }
        result = Vector3.Zero;
        return false;
    }

    private void CaptureScreenshot()
    {
        var texture = GetViewport().GetTexture();
        var image = texture?.GetImage();
        if (image == null || image.IsEmpty())
        {
            GD.PushWarning("ThreeBoats screenshot unavailable for this rendering backend.");
            return;
        }
        var path = "user://threeboats.png";
        image.SavePng(path);
        if (_liner != null)
            GD.Print($"THREEBOATS_LINER position={_liner.GlobalPosition} velocity={_liner.LinearVelocity}");
        if (CrestOceanRendererFacade.Instance is { } ocean)
            GD.Print($"THREEBOATS_DYNAMIC interactions={CrestSphereWaterInteraction.ActiveInteractions.Count} " +
                $"dispatches={ocean.SphereDispatchCount} damping={ocean.DynamicWaveDamping}");
        var activeInjections = 0;
        var maximumInjectionSpeed = 0.0f;
        foreach (var interaction in CrestSphereWaterInteraction.ActiveInteractions)
            if (interaction.TryGetInjection(out _, out var velocity, out _, out _))
            {
                activeInjections++;
                maximumInjectionSpeed = Mathf.Max(maximumInjectionSpeed, velocity.Length());
            }
        GD.Print($"THREEBOATS_INJECTIONS active={activeInjections} max_speed={maximumInjectionSpeed:F3}");
        GD.Print($"THREEBOATS_SCREENSHOT {ProjectSettings.GlobalizePath(path)}");
    }

    private void CaptureLodReplayFrame(int index, double simulationTime,
        float oceanScale, float scaleBlend, bool finalFrame)
    {
        var image = GetViewport().GetTexture()?.GetImage();
        if (image != null && !image.IsEmpty())
        {
            var path = $"{_lodReplayDirectory}/frame_{index:D4}.png";
            image.SavePng(path);
            var position = _camera?.GlobalPosition ?? Vector3.Zero;
            GD.Print($"THREEBOATS_LOD_FRAME {index:D4} time={simulationTime:F6} " +
                $"position={position} scale={oceanScale:F6} blend={scaleBlend:F6} " +
                $"path={ProjectSettings.GlobalizePath(path)}");
        }
        if (finalFrame) GetTree().Quit();
    }
}
