using Godot;

namespace Crest.Godot;

/// C# port of the CPU-side cascade transform bookkeeping used by Crest.
/// Arrays use the same two-vec4-per-cascade layout as the compute shaders.
[GlobalClass]
public partial class CrestLodTransformCs : RefCounted
{
    public const float RenderAboveSeaLevel = 10000.0f;
    public const float RenderBelowSeaLevel = 10000.0f;

    public int LodCount { get; }
    public int LodDataResolution { get; }
    public Vector2[] PosSnapped { get; }
    public float[] TexelWidth { get; }
    public float[] MaxWavelength { get; }
    public float[] CascadeDataCurrent { get; private set; }
    public float[] CascadeDataSource { get; private set; }

    public CrestLodTransformCs(int lodCount = CrestConstantsCs.DefaultLodCount,
        int resolution = CrestConstantsCs.DefaultLodDataResolution)
    {
        LodCount = Mathf.Clamp(lodCount, 1, CrestConstantsCs.MaxLodCount);
        LodDataResolution = Mathf.Max(1, resolution);
        PosSnapped = new Vector2[LodCount];
        TexelWidth = new float[LodCount];
        MaxWavelength = new float[LodCount];
        CascadeDataCurrent = new float[CrestConstantsCs.CascadeParamsCount * 8];
        CascadeDataSource = new float[CrestConstantsCs.CascadeParamsCount * 8];
    }

    public float CalcLodScale(int lodIndex, float oceanScale) =>
        oceanScale * Mathf.Pow(2.0f, lodIndex);

    public void UpdateTransforms(float oceanScale, Vector2 rootPosition)
    {
        (CascadeDataSource, CascadeDataCurrent) = (CascadeDataCurrent, CascadeDataSource);
        for (var i = 0; i < LodCount; i++)
        {
            var lodScale = CalcLodScale(i, oceanScale);
            var texel = 4.0f * lodScale / LodDataResolution;
            TexelWidth[i] = texel;
            PosSnapped[i] = new Vector2(
                Snap(rootPosition.X, texel), Snap(rootPosition.Y, texel));
            MaxWavelength[i] = texel * 4.0f;
        }

        for (var i = 0; i < CrestConstantsCs.CascadeParamsCount; i++)
        {
            var index = Mathf.Min(i, LodCount - 1);
            var weight = i >= LodCount ? 0.0f : 1.0f;
            var offset = i * 8;
            CascadeDataCurrent[offset] = PosSnapped[index].X;
            CascadeDataCurrent[offset + 1] = PosSnapped[index].Y;
            CascadeDataCurrent[offset + 2] = CalcLodScale(index, oceanScale);
            CascadeDataCurrent[offset + 3] = LodDataResolution;
            CascadeDataCurrent[offset + 4] = 1.0f / LodDataResolution;
            CascadeDataCurrent[offset + 5] = TexelWidth[index];
            CascadeDataCurrent[offset + 6] = weight;
            CascadeDataCurrent[offset + 7] = MaxWavelength[index];
        }
    }

    public float CascadeWorldSize(int lodIndex, float oceanScale) =>
        4.0f * CalcLodScale(lodIndex, oceanScale);

    public Rect2 GetValidRect(int lodIndex)
    {
        var width = TexelWidth[lodIndex] * LodDataResolution;
        var origin = PosSnapped[lodIndex] - new Vector2(width, width) * 0.5f;
        return new Rect2(origin + new Vector2(TexelWidth[lodIndex], TexelWidth[lodIndex]),
            new Vector2(width - 2.0f * TexelWidth[lodIndex], width - 2.0f * TexelWidth[lodIndex]));
    }

    public int SuggestDataLod(Vector2 worldPosition, int minLod = 0)
    {
        for (var i = Mathf.Clamp(minLod, 0, LodCount - 1); i < LodCount; i++)
            if (GetValidRect(i).HasPoint(worldPosition))
                return i;
        return -1;
    }

    private static float Snap(float value, float texel) =>
        value - Mathf.PosMod(value, texel);
}
