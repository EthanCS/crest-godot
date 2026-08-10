using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// C# port of Crest's RegisterLodDataInput base. Like Unity's component +
/// Renderer pairing, the Godot node is the renderer: Mesh stores the input
/// geometry and its ShaderMaterial stores the official Crest shader fields.
[Tool, GlobalClass]
public partial class CrestRegisterLodDataInput : MeshInstance3D, ICrestLodDataInputProvider
{
    private readonly System.Collections.Generic.Dictionary<string, (int Hash, Texture2D Texture)> _rasterCache = new();
    private int _activeSurface = -1;
    [Export] public bool _checkShaderName { get; set; } = true;
    [Export] public bool _checkShaderPasses { get; set; } = true;
    [Export] public bool _disableRenderer { get; set; } = true;
    public override void _Ready()
    {
        // Unity disables the normal Renderer after registering it with Crest.
        // Our compute managers read it directly, so it can be hidden normally.
        if (_disableRenderer) Visible = false;
    }

    public ShaderMaterial? GetInputMaterial()
    {
        if (MaterialOverride is ShaderMaterial material) return material;
        if (Mesh == null || Mesh.GetSurfaceCount() == 0) return null;
        var surface = _activeSurface >= 0 ? _activeSurface : 0;
        return Mesh.SurfaceGetMaterial(surface) as ShaderMaterial;
    }

    public string GetInputShaderName()
    {
        var path = GetInputMaterial()?.Shader?.ResourcePath ?? string.Empty;
        return path.GetFile().GetBaseName().ToLowerInvariant();
    }

    protected Variant MaterialParameter(string name) =>
        GetInputMaterial()?.GetShaderParameter(name) ?? default;

    protected float MaterialFloat(string name, float fallback = 0.0f)
    {
        var value = MaterialParameter(name);
        return value.VariantType is Variant.Type.Float or Variant.Type.Int ? (float)value : fallback;
    }

    protected Color MaterialColor(string name, Color fallback)
    {
        var value = MaterialParameter(name);
        return value.VariantType == Variant.Type.Color ? (Color)value : fallback;
    }

    protected Vector4 MaterialVector4(string name, Vector4 fallback)
    {
        var value = MaterialParameter(name);
        return value.VariantType == Variant.Type.Vector4 ? (Vector4)value : fallback;
    }

    protected Texture2D? MaterialTexture(string name)
    {
        var value = MaterialParameter(name);
        return value.VariantType == Variant.Type.Object ? value.AsGodotObject() as Texture2D : null;
    }

    private protected Texture2D RasterizedInput(string cacheKey, CrestInputRasterizer.ValueMode mode,
        Texture2D? source = null, Color? defaultSample = null, Vector4? textureSt = null,
        float cutoff = 0.0f)
    {
        var sample = defaultSample ?? Colors.White;
        var st = textureSt ?? new Vector4(1, 1, 0, 0);
        var hash = System.HashCode.Combine(System.HashCode.Combine(Mesh?.GetInstanceId() ?? 0UL,
                GlobalTransform.GetHashCode(), GetInputMaterial()?.GetInstanceId() ?? 0UL,
                source?.GetInstanceId() ?? 0UL),
            (int)mode, sample.GetHashCode(), st.GetHashCode(), cutoff, _activeSurface);
        var surfaceKey = $"{cacheKey}:{_activeSurface}";
        if (_rasterCache.TryGetValue(surfaceKey, out var cached) && cached.Hash == hash)
            return cached.Texture;
        var texture = CrestInputRasterizer.Rasterize(this, 256, mode, source, sample, st, cutoff, _activeSurface);
        _rasterCache[surfaceKey] = (hash, texture);
        return texture;
    }

    public Vector2 GetRectCenter()
    {
        GetWorldXzBounds(out var minimum, out var maximum);
        return (minimum + maximum) * 0.5f;
    }

    /// Unity's input shaders subtract the sampled horizontal water motion
    /// when FollowHorizontalMotion is disabled. Moving the projected input
    /// rectangle by the same amount is equivalent for our raster textures.
    protected Vector2 GetRectCenter(bool followHorizontalMotion)
    {
        var center = GetRectCenter();
        if (followHorizontalMotion) return center;
        var displacement = CrestCollisionCs.SampleDisplacement(center);
        return center - new Vector2(displacement.X, displacement.Z);
    }

    public Vector2 GetRectHalfSize()
    {
        GetWorldXzBounds(out var minimum, out var maximum);
        var half = (maximum - minimum) * 0.5f;
        return new Vector2(Mathf.Max(half.X, 0.001f), Mathf.Max(half.Y, 0.001f));
    }

    private void GetWorldXzBounds(out Vector2 minimum, out Vector2 maximum)
    {
        if (Mesh == null)
        {
            minimum = maximum = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            return;
        }
        CrestInputRasterizer.WorldBounds(this, out minimum, out maximum);
    }

    public virtual Dictionary GetInjection() => new();

    public virtual Array GetInjections()
    {
        var result = new Array();
        if (MaterialOverride != null || Mesh == null || Mesh.GetSurfaceCount() <= 1)
        {
            var injection = GetInjection();
            if (injection.Count > 0) result.Add(injection);
            return result;
        }

        try
        {
            for (var surface = 0; surface < Mesh.GetSurfaceCount(); surface++)
            {
                if (Mesh.SurfaceGetMaterial(surface) is not ShaderMaterial) continue;
                _activeSurface = surface;
                var injection = GetInjection();
                if (injection.Count > 0) result.Add(injection);
            }
        }
        finally
        {
            _activeSurface = -1;
        }
        return result;
    }
}
