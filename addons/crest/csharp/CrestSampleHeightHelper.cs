using Godot;

namespace Crest.Godot;

[GlobalClass]
public partial class CrestSampleHeightHelper : RefCounted
{
    public float height { get; private set; }
    public float velocity { get; private set; }

    public bool sample(Vector3 worldPosition)
    {
        var ocean = CrestOceanRendererFacade.Instance;
        if (ocean == null) return false;
        var sampled = CrestCollisionCs.SampleHeight(new Vector2(worldPosition.X, worldPosition.Z),
            ocean.CurrentTime, ocean.OceanLevel, out var result);
        height = result;
        return sampled;
    }

    public bool sample_height_and_velocity(Vector3 worldPosition, double delta = 0.0167)
    {
        var ocean = CrestOceanRendererFacade.Instance;
        if (ocean == null) return false;
        var sampled = CrestCollisionCs.SampleHeightAndVelocity(new Vector2(worldPosition.X, worldPosition.Z),
            ocean.CurrentTime, delta, ocean.OceanLevel, out var sampledHeight, out var sampledVelocity);
        height = sampledHeight;
        velocity = sampledVelocity;
        return sampled;
    }

    public float get_height_above(Vector3 worldPosition) =>
        sample(worldPosition) ? worldPosition.Y - height : 0.0f;
}
