using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# port of OceanWaveSpectrum used by Gerstner and FFT generators.
[Tool, GlobalClass]
public partial class CrestWaveSpectrum : Resource
{
    public const int NumOctaves = 14;
    public const int SmallestWavelengthPower = -4;
    private const float MinimumPowerLog = -8.0f;

    [Export] public int _version { get; set; }
    [Export] public float _fetch { get; set; } = 500000.0f;
    [Export(PropertyHint.Range, "0,180,0.1")] public float _waveDirectionVariance { get; set; } = 90.0f;
    [Export(PropertyHint.Range, "0,25,0.01")] public float _gravityScale { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,2,0.01")] public float _smallWavelengthMultiplier { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,10,0.01")] public float _multiplier { get; set; } = 1.0f;
    [Export] public float[] _powerLog { get; set; } =
        { -5.71f, -5.03f, -4.54f, -3.88f, -3.28f, -2.32f, -1.78f, -1.21f,
          -0.54f, 0.28f, 0.54f, 1.03f, 1.44f, -8.0f };
    [Export] public Array<bool> _powerDisabled { get; set; } = DisabledDefaults();
    [Export] public float[] _chopScales { get; set; } = Ones();
    [Export] public float[] _gravityScales { get; set; } = Ones();
    [Export(PropertyHint.Range, "0,2,0.01")] public float _chop { get; set; } = 1.6f;
    [Export] public bool _showAdvancedControls { get; set; }
    [Export] public int _model { get; set; }

    public static float SmallWavelength(int octave) => Mathf.Pow(2.0f, SmallestWavelengthPower + octave);

    public float get_amplitude(float wavelength, int componentsPerOctave,
        float windSpeed = 5.5555556f, float gravity = CrestConstantsCs.Gravity)
    {
        if (wavelength <= 0.0f) return 0.0f;
        var power2 = Mathf.Clamp(Mathf.Log(wavelength) / Mathf.Log(2.0f),
            SmallestWavelengthPower, SmallestWavelengthPower + NumOctaves - 1.0f);
        var floor = Mathf.Floor(power2);
        var index = Mathf.Clamp((int)floor - SmallestWavelengthPower, 0, NumOctaves - 1);
        var lambdaLow = Mathf.Pow(2.0f, floor);
        var alpha = (wavelength - lambdaLow) / lambdaLow;
        var thisPower = IsDisabled(index) ? MinimumPowerLog : GetPower(index);
        var next = Mathf.Min(index + 1, NumOctaves - 1);
        var nextPower = IsDisabled(next) ? MinimumPowerLog : GetPower(next);
        var power = Mathf.Pow(10.0f, Mathf.Lerp(thisPower, nextPower, alpha));
        var k = Mathf.Tau / wavelength;
        var omegaLow = Mathf.Sqrt(gravity * Mathf.Tau / lambdaLow);
        var omegaHigh = Mathf.Sqrt(gravity * Mathf.Tau / (2.0f * lambdaLow));
        var dOmega = (omegaLow - omegaHigh) / Mathf.Max(componentsPerOctave, 1);
        var wm = 0.87f * gravity / Mathf.Max(windSpeed, 0.01f);
        var omega = Mathf.Sqrt(gravity * k);
        power *= Mathf.Exp(-1.291f * Mathf.Pow(wm / omega, 4.0f));
        return Mathf.Sqrt(2.0f * power * dOmega) * 5.0f * _multiplier;
    }

    public Array<Dictionary> generate_wave_data(int componentsPerOctave, RandomNumberGenerator rng,
        float directionAngle = 0.0f)
    {
        var result = new Array<Dictionary>();
        var minimum = Mathf.Pow(2.0f, SmallestWavelengthPower);
        var count = Mathf.Max(componentsPerOctave, 1);
        for (var octave = 0; octave < NumOctaves; octave++)
        {
            for (var i = 0; i < count; i++)
            {
                var low = minimum * (1.0f + (float)i / count);
                var high = Mathf.Min(minimum * (1.0f + (float)(i + 1) / count), 2.0f * minimum);
                var random = ((float)i + rng.Randf()) / count;
                result.Add(new Dictionary
                {
                    ["wavelength"] = Mathf.Lerp(low, high, rng.Randf()),
                    ["angle_deg"] = (2.0f * random - 1.0f) * _waveDirectionVariance + directionAngle,
                    ["phase"] = Mathf.Tau * ((float)i + rng.Randf()) / count,
                });
            }
            minimum *= 2.0f;
        }
        return result;
    }

    public void apply_phillips_spectrum(float newWindSpeed, float gravity = CrestConstantsCs.Gravity)
    {
        EnsureArrays();
        for (var octave = 0; octave < NumOctaves; octave++)
        {
            var wavelength = SmallWavelength(octave);
            var omega = Mathf.Sqrt(gravity * Mathf.Tau / wavelength);
            var spectrum = 8.1e-3f * gravity * gravity / Mathf.Pow(omega, 5.0f);
            _powerLog[octave] = Mathf.Max(Mathf.Log(spectrum) / Mathf.Log(10.0f), MinimumPowerLog);
        }
        EmitChanged();
    }

    private float GetPower(int index) => index < _powerLog.Length ? _powerLog[index] : MinimumPowerLog;
    private bool IsDisabled(int index) => index < _powerDisabled.Count && _powerDisabled[index];
    private void EnsureArrays()
    {
        if (_powerLog.Length >= NumOctaves) return;
        var resized = new float[NumOctaves]; System.Array.Copy(_powerLog, resized, _powerLog.Length); _powerLog = resized;
    }
    private static float[] Ones()
    {
        var result = new float[NumOctaves]; System.Array.Fill(result, 1.0f); return result;
    }
    private static Array<bool> DisabledDefaults()
    {
        var result = new Array<bool>(); for (var i = 0; i < NumOctaves; i++) result.Add(false); return result;
    }
}
