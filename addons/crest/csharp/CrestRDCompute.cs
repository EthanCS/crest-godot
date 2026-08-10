using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// RenderingDevice compute helper. This mirrors the GDScript helper while
/// keeping RID lifetime and push-constant alignment explicit in C#.
[GlobalClass]
public partial class CrestRDComputeCs : RefCounted
{
    private static readonly List<(RenderingDevice Device, Rid Rid, int Frames, bool UniformSet)> Pending = new();

    private RenderingDevice? _device;
    private Rid _shader;
    private Rid _pipeline;

    public bool IsValid => _pipeline.IsValid;
    public Rid ShaderRid => _shader;
    public Rid PipelineRid => _pipeline;

    public static CrestRDComputeCs? FromFile(RenderingDevice device, string path)
    {
        var filesystemPath = path.StartsWith("res://", StringComparison.Ordinal) ?
            ProjectSettings.GlobalizePath(path) : path;
        var source = ResolveIncludes(filesystemPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return string.IsNullOrEmpty(source) ? null : FromSource(device, source, path);
    }

    public static CrestRDComputeCs? FromSource(RenderingDevice device, string source, string debugName = "")
    {
        var marker = "#[compute]";
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex >= 0)
            source = source[(markerIndex + marker.Length)..];
        var lines = source.Split('\n');
        var version = System.Array.FindIndex(lines, line => line.TrimStart().StartsWith("#version", StringComparison.Ordinal));
        if (version >= 0)
        {
            var versionLine = lines[version].Trim();
            var body = new List<string>(lines);
            body.RemoveAt(version);
            source = versionLine + "\n" + string.Join("\n", body);
        }

        var shaderSource = new RDShaderSource
        {
            Language = RenderingDevice.ShaderLanguage.Glsl,
            SourceCompute = source,
        };
        var spirv = device.ShaderCompileSpirVFromSource(shaderSource, false);
        if (!string.IsNullOrEmpty(spirv.CompileErrorCompute))
        {
            GD.PushError($"CrestRDComputeCs: compile error in {debugName}:\n{spirv.CompileErrorCompute}");
            return null;
        }

        var result = new CrestRDComputeCs { _device = device };
        result._shader = device.ShaderCreateFromSpirV(spirv, debugName);
        if (!result._shader.IsValid)
            return null;
        result._pipeline = device.ComputePipelineCreate(result._shader);
        return result;
    }

    public Rid MakeUniformSet(Array<RDUniform> uniforms, uint setIndex = 0) =>
        _device!.UniformSetCreate(uniforms, _shader, setIndex);

    public void Dispatch(uint groupsX, uint groupsY, uint groupsZ,
        System.Collections.Generic.Dictionary<uint, Rid> sets, byte[]? pushConstants = null)
    {
        var list = _device!.ComputeListBegin();
        _device.ComputeListBindComputePipeline(list, _pipeline);
        foreach (var pair in sets)
            _device.ComputeListBindUniformSet(list, pair.Value, pair.Key);
        if (pushConstants is { Length: > 0 })
            _device.ComputeListSetPushConstant(list, pushConstants, (uint)pushConstants.Length);
        _device.ComputeListDispatch(list, groupsX, groupsY, groupsZ);
        _device.ComputeListEnd();
    }

    public static byte[] PackPushConstants(IReadOnlyList<float> values)
    {
        var size = (values.Count * sizeof(float) + 15) & ~15;
        var bytes = new byte[size];
        for (var i = 0; i < values.Count; i++)
            Buffer.BlockCopy(BitConverter.GetBytes(values[i]), 0, bytes, i * sizeof(float), sizeof(float));
        return bytes;
    }

    public static void FreeRidDeferred(RenderingDevice device, Rid rid)
        => ScheduleDeferredFree(device, rid, false);

    public static void FreeUniformSetDeferred(RenderingDevice device, Rid rid)
        => ScheduleDeferredFree(device, rid, true);

    private static void ScheduleDeferredFree(RenderingDevice device, Rid rid, bool uniformSet)
    {
        if (!rid.IsValid)
            return;

        // RenderingDevice caches identical uniform sets and may return the
        // same RID for several dispatches. Keep a single pending release and
        // extend its lifetime when that RID is used again.
        for (var i = 0; i < Pending.Count; i++)
        {
            var item = Pending[i];
            if (ReferenceEquals(item.Device, device) && item.Rid.Id == rid.Id)
            {
                Pending[i] = (item.Device, item.Rid, 4, item.UniformSet || uniformSet);
                return;
            }
        }

        Pending.Add((device, rid, 4, uniformSet));
    }

    public static void FlushDeferredFrees()
    {
        for (var i = 0; i < Pending.Count;)
        {
            var item = Pending[i];
            if (item.Frames <= 1)
            {
                // Uniform sets are invalidated automatically when any bound
                // resource or their shader is destroyed.
                if (!item.UniformSet || item.Device.UniformSetIsValid(item.Rid))
                    item.Device.FreeRid(item.Rid);
                Pending.RemoveAt(i);
            }
            else
            {
                Pending[i] = (item.Device, item.Rid, item.Frames - 1, item.UniformSet);
                i++;
            }
        }
    }

    public void DisposeRid()
    {
        if (_device == null)
            return;
        if (_shader.IsValid)
            FreeRidDeferred(_device, _shader);
        _shader = new Rid();
        _pipeline = new Rid();
    }

    private static string ResolveIncludes(string path, HashSet<string> seen)
    {
        if (!seen.Add(path) || !File.Exists(path))
            return string.Empty;
        var baseDir = Path.GetDirectoryName(path) ?? string.Empty;
        var output = new StringBuilder();
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#include", StringComparison.Ordinal))
            {
                var include = trimmed[8..].Trim().Trim('"');
                output.AppendLine(ResolveIncludes(Path.Combine(baseDir, include), seen));
            }
            else
                output.AppendLine(line);
        }
        return output.ToString();
    }
}
