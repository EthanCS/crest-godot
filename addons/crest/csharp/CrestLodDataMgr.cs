using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// Base C# simulation data manager. Owns one or two array textures and keeps
/// the material bridge pointed at the current ping-pong target.
[GlobalClass]
public partial class CrestLodDataMgrCs : RefCounted
{
    public RenderingDevice? Device { get; private set; }
    public int Resolution { get; private set; }
    public int LayerCount { get; private set; }
    public RenderingDevice.DataFormat DataFormat { get; private set; }
    public Texture2DArrayRD TextureArray { get; } = new();
    public Rid Sampler { get; private set; }

    private readonly Array<Rid> _textures = new();
    private int _current;

    public void InitSim(int resolution, int layers, RenderingDevice.DataFormat format,
        bool doubleBuffered, Color initialColor = default)
    {
        Device = RenderingServer.GetRenderingDevice();
        if (Device == null)
            return;
        Resolution = resolution;
        LayerCount = layers;
        DataFormat = format;
        var bytesPerPixel = PixelSize(format);
        var layerData = new byte[bytesPerPixel * resolution * resolution];
        FillInitial(layerData, format, initialColor);
        var layerBlocks = new Array<byte[]>();
        for (var i = 0; i < layers; i++)
            layerBlocks.Add((byte[])layerData.Clone());
        var count = doubleBuffered ? 2 : 1;
        var formatDesc = new RDTextureFormat
        {
            Format = format,
            Width = (uint)resolution,
            Height = (uint)resolution,
            Depth = 1,
            ArrayLayers = (uint)layers,
            TextureType = RenderingDevice.TextureType.Type2DArray,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit |
                RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.CanUpdateBit |
                RenderingDevice.TextureUsageBits.CanCopyFromBit |
                RenderingDevice.TextureUsageBits.CanCopyToBit,
        };
        for (var i = 0; i < count; i++)
            _textures.Add(Device.TextureCreate(formatDesc, new RDTextureView(), layerBlocks));
        if (!Sampler.IsValid)
        {
            var samplerState = new RDSamplerState
            {
                MinFilter = RenderingDevice.SamplerFilter.Linear,
                MagFilter = RenderingDevice.SamplerFilter.Linear,
                RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
                RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            };
            Sampler = Device.SamplerCreate(samplerState);
        }
        TextureArray.TextureRdRid = _textures[_current];
    }

    public Rid CurrentTexture() => _textures.Count == 0 ? new Rid() : _textures[_current];
    public Rid TargetTexture() => _textures.Count == 0 ? new Rid() : _textures[(_current + 1) % _textures.Count];

    public void SwapTargets()
    {
        if (_textures.Count > 1)
        {
            _current = (_current + 1) % _textures.Count;
            TextureArray.TextureRdRid = _textures[_current];
        }
    }

    public RDUniform MakeSampledUniform(uint binding, bool useTarget = false)
    {
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = (int)binding };
        uniform.AddId(Sampler);
        uniform.AddId(useTarget ? TargetTexture() : CurrentTexture());
        return uniform;
    }

    public RDUniform MakeImageUniform(uint binding, bool useTarget = true)
    {
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = (int)binding };
        uniform.AddId(useTarget ? TargetTexture() : CurrentTexture());
        return uniform;
    }

    public void FreeRids()
    {
        TextureArray.TextureRdRid = new Rid();
        if (Device != null)
        {
            foreach (var texture in _textures)
                if (texture.IsValid) CrestRDComputeCs.FreeRidDeferred(Device, texture);
            if (Sampler.IsValid) CrestRDComputeCs.FreeRidDeferred(Device, Sampler);
        }
        _textures.Clear();
        Sampler = new Rid();
    }

    private static int PixelSize(RenderingDevice.DataFormat format) => format switch
    {
        RenderingDevice.DataFormat.R16G16B16A16Sfloat => 8,
        RenderingDevice.DataFormat.R32G32B32A32Sfloat => 16,
        RenderingDevice.DataFormat.R32G32Sfloat => 8,
        RenderingDevice.DataFormat.R16G16Sfloat => 4,
        RenderingDevice.DataFormat.R16Sfloat => 2,
        RenderingDevice.DataFormat.R8G8B8A8Unorm => 4,
        RenderingDevice.DataFormat.R8G8Unorm => 2,
        RenderingDevice.DataFormat.R8Unorm => 1,
        _ => 8,
    };

    private static void FillInitial(byte[] data, RenderingDevice.DataFormat format, Color color)
    {
        if (color == default) return;
        var size = PixelSize(format);
        for (var offset = 0; offset < data.Length; offset += size)
        {
            switch (format)
            {
                case RenderingDevice.DataFormat.R8Unorm:
                    data[offset] = (byte)Mathf.Clamp(Mathf.RoundToInt(color.R * 255.0f), 0, 255);
                    break;
                case RenderingDevice.DataFormat.R8G8Unorm:
                    data[offset] = (byte)Mathf.Clamp(Mathf.RoundToInt(color.R * 255.0f), 0, 255);
                    data[offset + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(color.G * 255.0f), 0, 255);
                    break;
                case RenderingDevice.DataFormat.R8G8B8A8Unorm:
                    data[offset] = (byte)Mathf.Clamp(Mathf.RoundToInt(color.R * 255.0f), 0, 255);
                    data[offset + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(color.G * 255.0f), 0, 255);
                    data[offset + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(color.B * 255.0f), 0, 255);
                    data[offset + 3] = (byte)Mathf.Clamp(Mathf.RoundToInt(color.A * 255.0f), 0, 255);
                    break;
            }
        }
    }
}
