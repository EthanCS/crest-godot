namespace Crest.Godot;

/// Constants shared by the C# migration and compute shader bindings.
public static class CrestConstantsCs
{
    public const int MaxLodCount = 15;
    public const int CascadeParamsCount = MaxLodCount + 1;
    public const int DefaultLodDataResolution = 256;
    public const int DefaultLodCount = 7;
    public const float Gravity = 9.81f;
    public const int ThreadGroupSize = 8;
    public const float SssMaximum = 0.6f;
    public const float SssRange = 0.12f;
    public const float OceanDepthBaseline = -100000.0f;
    public const float MaskAboveSurface = 1.0f;
    public const float MaskBelowSurface = -1.0f;
    public const float MaskBelowSurfaceCulled = -2.0f;
}
