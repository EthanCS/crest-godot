using Godot;

/// Free-flight camera for the Three Boats demo.
/// Click to capture the mouse, Escape to release it, WASD/QE to move.
public partial class ThreeBoatsFlyCamera : Camera3D
{
    [Export] public float MoveSpeed { get; set; } = 20.0f;
    [Export] public float MouseSensitivity { get; set; } = 0.0025f;
    public bool NavigationEnabled { get; set; } = true;

    private float _yaw;
    private float _pitch;

    public override void _Ready() => SyncLookAngles();

    public void SyncLookAngles()
    {
        _yaw = Rotation.Y;
        _pitch = Rotation.X;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!NavigationEnabled) return;
        if (inputEvent is InputEventMouseButton button && button.Pressed)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
        }
        else if (inputEvent is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().SetInputAsHandled();
        }
        else if (inputEvent is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= motion.Relative.X * MouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, -1.45f, 1.45f);
            Rotation = new Vector3(_pitch, _yaw, 0.0f);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!NavigationEnabled) return;
        var direction = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W)) direction -= GlobalBasis.Z;
        if (Input.IsKeyPressed(Key.S)) direction += GlobalBasis.Z;
        if (Input.IsKeyPressed(Key.A)) direction -= GlobalBasis.X;
        if (Input.IsKeyPressed(Key.D)) direction += GlobalBasis.X;
        if (Input.IsKeyPressed(Key.Q)) direction -= Vector3.Up;
        if (Input.IsKeyPressed(Key.E)) direction += Vector3.Up;
        if (direction.IsZeroApprox()) return;
        var speed = MoveSpeed * (Input.IsKeyPressed(Key.Shift) ? 4.0f : 1.0f);
        GlobalPosition += direction.Normalized() * speed * (float)delta;
    }
}
