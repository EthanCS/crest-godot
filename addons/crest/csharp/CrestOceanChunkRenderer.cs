using Godot;

namespace Crest.Godot;

/// C# ocean tile renderer with per-instance cascade selection and expanded
/// displacement bounds.
[Tool, GlobalClass]
public partial class CrestOceanChunkRendererCs : MeshInstance3D
{
    public int LodIndex { get; private set; }

    public void Setup(int lodIndex)
    {
        LodIndex = lodIndex;
        SetInstanceShaderParameter("ld_slice_index", (float)lodIndex);
    }

    public void ExpandBounds(float maxHorizontalDisplacement, float maxVerticalDisplacement)
    {
        if (Mesh == null)
            return;
        var bounds = Mesh.GetAabb();
        var horizontal = maxHorizontalDisplacement / Mathf.Max(Scale.X, 0.001f);
        bounds.Position -= new Vector3(horizontal, maxVerticalDisplacement, horizontal);
        bounds.Size += new Vector3(2.0f * horizontal, 2.0f * maxVerticalDisplacement, 2.0f * horizontal);
        CustomAabb = bounds;
    }
}
