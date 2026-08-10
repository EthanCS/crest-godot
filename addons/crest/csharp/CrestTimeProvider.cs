using Godot;

namespace Crest.Godot;

/// <summary>
/// C# implementation of Crest's simulation clock. Kept API-compatible with
/// the GDScript provider so callers can migrate one subsystem at a time.
/// </summary>
[GlobalClass]
public partial class CrestTimeProviderCs : RefCounted
{
    public static CrestTimeProviderCs? GlobalProvider { get; set; }

    // Lower-case wrappers keep calls source-compatible with the GDScript API.
    public static CrestTimeProviderCs? get_global_provider() => GlobalProvider;
    public static void set_global_provider(CrestTimeProviderCs? value) => GlobalProvider = value;

    [Export] public bool UseCustomTime { get; set; }
    [Export] public double CustomTime { get; set; }
    [Export] public double TimeScale { get; set; } = 1.0;
    [Export] public bool Paused { get; set; }

    private double _time;

    public void Advance(double delta)
    {
        if (Paused)
            return;
        if (UseCustomTime)
            _time = CustomTime;
        else
            _time += delta * TimeScale;
    }

    public double CurrentTime() => UseCustomTime ? CustomTime : _time;

    public void advance(double delta) => Advance(delta);
    public double current_time() => CurrentTime();

    public void Reset(double value = 0.0)
    {
        _time = value;
        if (UseCustomTime)
            CustomTime = value;
    }
}
