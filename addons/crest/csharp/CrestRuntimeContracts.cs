using Godot;
using Godot.Collections;

namespace Crest.Godot;

/// Strongly typed runtime contract for nodes that inject data into a Crest
/// simulation. Groups are still used for discovery, but invocation never
/// depends on a method-name string.
public interface ICrestLodDataInputProvider
{
    Array GetInjections();
}

/// Strongly typed contract shared by analytic and FFT wave generators.
public interface ICrestShapeGenerator
{
    void Evaluate(Rid target, CrestSeaFloorDepthManagerCs? depthManager,
        CrestLodTransformCs lodTransform, double oceanScale, double oceanLevel,
        double time, bool accumulate = false);
}
