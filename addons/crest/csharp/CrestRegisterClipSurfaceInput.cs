using Godot;
using Godot.Collections;

#pragma warning disable CS8604
namespace Crest.Godot;

[Tool, GlobalClass]
public partial class CrestRegisterClipSurfaceInput : CrestRegisterLodDataInput
{
    [Export] public int _version { get; set; } = 1;
    [Export] public int _mode { get; set; } = 1;
    [Export] public int _primitive { get; set; } = 3;
    [Export] public int _order { get; set; }
    [Export] public bool _inverted { get; set; }
    [Export] public bool _disableClipSurfaceWhenTooFarFromSurface { get; set; }
    [Export] public uint _animatedWavesDisplacementSamplingIterations { get; set; } = 4;
    public override void _EnterTree() => AddToGroup("crest_clip_input");
    public override Dictionary GetInjection()
    {
        if (_disableClipSurfaceWhenTooFarFromSurface && TooFarFromWater()) return new Dictionary();

        if (_mode == 1)
        {
            PrimitiveBounds(out var center, out var half);
            var inverse = GlobalTransform.AffineInverse();
            return new Dictionary
            {
                ["rect_center"] = center, ["rect_half_size"] = half,
                ["mode"] = _inverted ? 1.0f : 0.0f, ["primitive"] = (float)_primitive,
                ["primitive_inverse"] = inverse, ["displacement_iterations"] = (float)_animatedWavesDisplacementSamplingIterations,
                ["order"] = _order,
            };
        }

        var source = MaterialTexture("_MainTex");
        var texture = RasterizedInput("clip", source != null ? CrestInputRasterizer.ValueMode.Texture :
            CrestInputRasterizer.ValueMode.Coverage, source, Colors.White,
            MaterialVector4("_MainTex_ST", new Vector4(1, 1, 0, 0)));
        return new Dictionary
        {
            ["rect_center"] = GetRectCenter(), ["rect_half_size"] = GetRectHalfSize(),
            ["mode"] = _inverted ? 1.0f : 0.0f, ["texture"] = texture, ["primitive"] = -1.0f,
            ["displacement_iterations"] = (float)_animatedWavesDisplacementSamplingIterations,
            ["order"] = _order,
        };
    }

    private void PrimitiveBounds(out Vector2 center, out Vector2 half)
    {
        var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var index = 0; index < 8; index++)
        {
            var local = new Vector3((index & 1) == 0 ? -0.5f : 0.5f,
                (index & 2) == 0 ? -0.5f : 0.5f, (index & 4) == 0 ? -0.5f : 0.5f);
            var world = GlobalTransform * local;
            minimum = new Vector2(Mathf.Min(minimum.X, world.X), Mathf.Min(minimum.Y, world.Z));
            maximum = new Vector2(Mathf.Max(maximum.X, world.X), Mathf.Max(maximum.Y, world.Z));
        }
        center = (minimum + maximum) * 0.5f;
        half = (maximum - minimum) * 0.5f;
        half = new Vector2(Mathf.Max(half.X, 0.001f), Mathf.Max(half.Y, 0.001f));
    }

    private bool TooFarFromWater()
    {
        var ocean = CrestOceanRendererFacade.Instance;
        if (ocean == null) return false;
        var xz = new Vector2(GlobalPosition.X, GlobalPosition.Z);
        CrestCollisionCs.SampleHeight(xz, ocean.CurrentTime, ocean.OceanLevel, out var waterHeight);
        var localWater = GlobalTransform.AffineInverse() * new Vector3(xz.X, waterHeight, xz.Y);
        if (_mode == 1)
            return Mathf.Abs(localWater.Y) > 0.5f + 1.0f / Mathf.Max(GlobalTransform.Basis.Y.Length(), 0.0001f);
        if (Mesh == null) return true;
        var bounds = Mesh.GetAabb();
        return Mathf.Abs(localWater.Y - Mathf.Clamp(localWater.Y, bounds.Position.Y, bounds.End.Y)) >
            1.0f / Mathf.Max(GlobalTransform.Basis.Y.Length(), 0.0001f);
    }
}
#pragma warning restore CS8604
