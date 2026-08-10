using Godot;

namespace Crest.Godot;

/// C# analytic collision/query API for the migrated Gerstner components.
[GlobalClass]
public partial class CrestCollisionCs : RefCounted
{
    public static Vector3 SampleDisplacement(Vector2 worldXz)
    {
        var ocean = CrestOceanRendererFacade.Instance;
        return ocean != null ? SampleDisplacement(worldXz, ocean.CurrentTime) : Vector3.Zero;
    }

    public static Vector3 SampleDisplacement(Vector2 worldXz, double time)
    {
        var result = Vector3.Zero;
        foreach (var shape in CrestShapeGerstner.ActiveShapes)
            result += shape.ComputeDisplacement(worldXz, time);
        return result;
    }

    public static bool SampleHeight(Vector2 worldXz, double time, float oceanLevel, out float height)
    {
        height = oceanLevel + SampleDisplacement(worldXz, time).Y;
        return true;
    }

    public static bool SampleHeight(Vector2 worldXz, out float height)
    {
        var ocean = CrestOceanRendererFacade.Instance;
        if (ocean == null) { height = 0.0f; return false; }
        return SampleHeight(worldXz, ocean.CurrentTime, ocean.OceanLevel, out height);
    }

    public static Vector3 SampleNormal(Vector2 worldXz, double time, float epsilon = 0.1f)
    {
        var center = SampleDisplacement(worldXz, time);
        var dx = SampleDisplacement(worldXz + new Vector2(epsilon, 0), time);
        var dz = SampleDisplacement(worldXz + new Vector2(0, epsilon), time);
        var tangentX = new Vector3(epsilon, 0, 0) + dx - center;
        var tangentZ = new Vector3(0, 0, epsilon) + dz - center;
        return tangentZ.Cross(tangentX).Normalized();
    }

    public static Vector3 SampleNormal(Vector2 worldXz, float epsilon = 0.1f)
    {
        var ocean = CrestOceanRendererFacade.Instance;
        return ocean != null ? SampleNormal(worldXz, ocean.CurrentTime, epsilon) : Vector3.Up;
    }

    public static bool SampleHeightAndVelocity(Vector2 worldXz, double time, double delta,
        float oceanLevel, out float height, out float velocity)
    {
        var current = SampleDisplacement(worldXz, time);
        var previous = SampleDisplacement(worldXz, time - delta);
        height = oceanLevel + current.Y;
        velocity = (current.Y - previous.Y) / Mathf.Max((float)delta, 1e-5f);
        return true;
    }

    public static bool SampleHeightAndVelocity(Vector2 worldXz, double delta,
        out float height, out float velocity)
    {
        var ocean = CrestOceanRendererFacade.Instance;
        if (ocean == null) { height = 0.0f; velocity = 0.0f; return false; }
        return SampleHeightAndVelocity(worldXz, ocean.CurrentTime, delta, ocean.OceanLevel,
            out height, out velocity);
    }
}
