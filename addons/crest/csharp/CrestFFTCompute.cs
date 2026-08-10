using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# GPU FFT pipeline for the 16 tiled displacement cascades.
[GlobalClass]
public partial class CrestFFTComputeCs : RefCounted
{
    public const int CascadeCount = 16;
    public int Resolution { get; private set; } = 128;
    public Rid WaveBuffers { get; private set; }
    public int SampleDispatchCount { get; private set; }
    private RenderingDevice? _device;
    private Rid _spectrumInit, _specH, _specX, _specZ, _tempH, _tempX, _tempZ, _controls, _sampler;
    private CrestRDComputeCs? _init, _update, _ifft, _sample;
    private int _log2;
    private readonly System.Collections.Generic.Dictionary<string, Rid> _sets = new();

    public bool Initialize(int resolution)
    {
        _device = RenderingServer.GetRenderingDevice();
        if (_device == null) return false;
        var exponent = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(Mathf.Max(resolution, 16)) /
            Mathf.Log(2.0f)), 4, 9);
        Resolution = 1 << exponent;
        _log2 = Mathf.RoundToInt(Mathf.Log(Resolution) / Mathf.Log(2.0f));
        _spectrumInit = MakeTexture(RenderingDevice.DataFormat.R32G32B32A32Sfloat);
        _specH = MakeTexture(RenderingDevice.DataFormat.R32G32Sfloat);
        _specX = MakeTexture(RenderingDevice.DataFormat.R32G32Sfloat);
        _specZ = MakeTexture(RenderingDevice.DataFormat.R32G32Sfloat);
        _tempH = MakeTexture(RenderingDevice.DataFormat.R32G32Sfloat);
        _tempX = MakeTexture(RenderingDevice.DataFormat.R32G32Sfloat);
        _tempZ = MakeTexture(RenderingDevice.DataFormat.R32G32Sfloat);
        WaveBuffers = MakeTexture(RenderingDevice.DataFormat.R16G16B16A16Sfloat);
        _controls = _device.StorageBufferCreate(CrestWaveSpectrum.NumOctaves * 4u);
        _sampler = _device.SamplerCreate(new RDSamplerState
        {
            RepeatU = RenderingDevice.SamplerRepeatMode.Repeat,
            RepeatV = RenderingDevice.SamplerRepeatMode.Repeat,
        });
        _init = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/fft_spectrum_init.glsl");
        _update = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/fft_spectrum_update.glsl");
        _ifft = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/fft_ifft_pass.glsl");
        _sample = CrestRDComputeCs.FromFile(_device, "res://addons/crest/shaders/sim/fft_to_wave_buffer.glsl");
        return _init?.IsValid == true && _update?.IsValid == true && _ifft?.IsValid == true && _sample?.IsValid == true;
    }

    public void RebuildSpectrum(CrestWaveSpectrum spectrum, Vector2 windDirection, float windSpeed,
        float turbulence, float seed)
    {
        if (_device == null || _init == null) return;
        var controls = new float[CrestWaveSpectrum.NumOctaves];
        for (var i = 0; i < controls.Length; i++)
            controls[i] = i < spectrum._powerDisabled.Count && spectrum._powerDisabled[i] ? 0.0f :
                Mathf.Pow(10.0f, i < spectrum._powerLog.Length ? spectrum._powerLog[i] : -8.0f) * spectrum._multiplier * spectrum._multiplier;
        _device.BufferUpdate(_controls, 0, (uint)(controls.Length * 4), FloatsToBytes(controls));
        var set = _init.MakeUniformSet(new Array<RDUniform> { ImageUniform(0, _spectrumInit), StorageUniform(1, _controls) });
        var push = new[] { (float)Resolution, (float)CascadeCount, CrestConstantsCs.Gravity * spectrum._gravityScale,
            windSpeed, windDirection.X, windDirection.Y, turbulence, seed };
        _init.Dispatch(Groups(), Groups(), CascadeCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set }, CrestRDComputeCs.PackPushConstants(push));
        CrestRDComputeCs.FreeUniformSetDeferred(_device, set);
    }

    public void AdvanceTime(float time, float gravityScale, float chop)
    {
        if (_device == null || _update == null || _ifft == null) return;
        var initial = SampleUniform(0, _spectrumInit);
        var updateSet = _update.MakeUniformSet(new Array<RDUniform>
        {
            initial, ImageUniform(1, _specH), ImageUniform(2, _specX), ImageUniform(3, _specZ),
        });
        _update.Dispatch(Groups(), Groups(), CascadeCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = updateSet },
            CrestRDComputeCs.PackPushConstants(new[] { (float)Resolution, (float)CascadeCount,
                CrestConstantsCs.Gravity * gravityScale, time, chop }));
        CrestRDComputeCs.FreeUniformSetDeferred(_device, updateSet);
        var list = _device.ComputeListBegin();
        _device.ComputeListBindComputePipeline(list, _ifft.PipelineRid);
        var total = _log2 * 2;
        for (var pass = 0; pass < total; pass++)
        {
            var row = pass < _log2; var local = row ? pass : pass - _log2;
            var final = pass == total - 1; var even = pass % 2 == 0;
            var key = (even ? "a" : "b") + (final ? "f" : "");
            if (!_sets.TryGetValue(key, out var set))
            {
                set = _ifft.MakeUniformSet(new Array<RDUniform>
                {
                    ImageUniform(0, even ? _specH : _tempH), ImageUniform(1, even ? _specX : _tempX),
                    ImageUniform(2, even ? _specZ : _tempZ), ImageUniform(3, even ? _tempH : _specH),
                    ImageUniform(4, even ? _tempX : _specX), ImageUniform(5, even ? _tempZ : _specZ),
                    ImageUniform(6, WaveBuffers),
                });
                _sets[key] = set;
            }
            _device.ComputeListBindUniformSet(list, set, 0);
            var push = CrestRDComputeCs.PackPushConstants(new[] { (float)Resolution, (float)_log2,
                (float)local, row ? 0.0f : 1.0f, final ? 1.0f : 0.0f });
            _device.ComputeListSetPushConstant(list, push, (uint)push.Length);
            _device.ComputeListDispatch(list, (uint)(Resolution / 2 / CrestConstantsCs.ThreadGroupSize), Groups(), CascadeCount);
            _device.ComputeListAddBarrier(list);
        }
        _device.ComputeListEnd();
    }

    public void SampleIntoWaveBuffer(Rid target, Rid cascadeBuffer, Rid sliceMap, int lodResolution,
        int lodCount, bool accumulate, float weight, float variance)
    {
        if (_device == null || _sample == null) return;
        var set = _sample.MakeUniformSet(new Array<RDUniform>
        {
            SampleUniform(0, WaveBuffers), ImageUniform(1, target), StorageUniform(2, sliceMap),
            StorageUniform(3, cascadeBuffer),
        });
        var groups = (uint)Mathf.CeilToInt(lodResolution / 8.0f);
        _sample.Dispatch(groups, groups, (uint)lodCount,
            new System.Collections.Generic.Dictionary<uint, Rid> { [0] = set },
            CrestRDComputeCs.PackPushConstants(new[] { (float)lodResolution, (float)lodCount,
                accumulate ? 1.0f : 0.0f, weight, variance }));
        SampleDispatchCount++;
        CrestRDComputeCs.FreeUniformSetDeferred(_device, set);
    }

    public void FreeRids()
    {
        _init?.DisposeRid(); _update?.DisposeRid(); _ifft?.DisposeRid(); _sample?.DisposeRid();
        if (_device != null)
        {
            foreach (var rid in new[] { _spectrumInit, _specH, _specX, _specZ, _tempH, _tempX, _tempZ, WaveBuffers, _controls, _sampler })
                if (rid.IsValid) CrestRDComputeCs.FreeRidDeferred(_device, rid);
        }
        _sets.Clear();
    }

    private Rid MakeTexture(RenderingDevice.DataFormat dataFormat)
    {
        var format = new RDTextureFormat { Format = dataFormat, Width = (uint)Resolution, Height = (uint)Resolution,
            ArrayLayers = CascadeCount, TextureType = RenderingDevice.TextureType.Type2DArray,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.CanUpdateBit };
        return _device!.TextureCreate(format, new RDTextureView());
    }
    private uint Groups() => (uint)Mathf.CeilToInt(Resolution / 8.0f);
    private RDUniform SampleUniform(uint binding, Rid rid)
    {
        var u = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = (int)binding };
        u.AddId(_sampler); u.AddId(rid); return u;
    }
    private static RDUniform ImageUniform(uint binding, Rid rid)
    { var u = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = (int)binding }; u.AddId(rid); return u; }
    private static RDUniform StorageUniform(uint binding, Rid rid)
    { var u = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = (int)binding }; u.AddId(rid); return u; }
    private static byte[] FloatsToBytes(float[] values)
    { var bytes = new byte[values.Length * 4]; System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length); return bytes; }
}
