using Godot;

namespace Crest.Godot;

/// <summary>
/// C# implementation of Crest's simulation clock.
/// </summary>
[GlobalClass]
public partial class CrestTimeProviderCs : RefCounted
{
    public static CrestTimeProviderCs? GlobalProvider { get; set; }

    [Export] public int _version { get; set; }
    [Export] public bool _paused { get; set; }
    [Export] public bool _overrideTime { get; set; }
    [Export] public float _time { get; set; }
    [Export] public bool _overrideDeltaTime { get; set; }
    [Export] public float _deltaTime { get; set; }
    public bool UseCustomTime { get => _overrideTime; set => _overrideTime = value; }
    public double CustomTime { get => _time; set => _time = (float)value; }
    public double TimeScale { get; set; } = 1.0;
    public bool Paused { get => _paused; set => _paused = value; }

    private double _timeInternal;

    public void Advance(double delta)
    {
        if (Paused)
            return;
        if (UseCustomTime)
            _timeInternal = CustomTime;
        else
            _timeInternal += (_overrideDeltaTime ? _deltaTime : delta) * TimeScale;
    }

    public double CurrentTime() => UseCustomTime ? CustomTime : _timeInternal;

    public void Reset(double value = 0.0)
    {
        _timeInternal = value;
        if (UseCustomTime)
            CustomTime = value;
    }
}
