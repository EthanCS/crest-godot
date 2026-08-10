using Godot;

namespace Crest.Godot;

/// <summary>
/// C# editor entry point. Runtime node registration is owned by the .NET
/// plugin assembly and scripts call the C# API directly.
/// </summary>
[Tool]
public partial class CrestPlugin : EditorPlugin
{
    private static readonly (string ScriptPath, string BaseType, string Name)[] Types =
    {
        ("res://addons/crest/csharp/CrestOceanRendererFacade.cs", "Node3D", "CrestOceanRenderer"),
        ("res://addons/crest/csharp/CrestOceanDebugGui.cs", "Node3D", "CrestOceanDebugGui"),
        ("res://addons/crest/csharp/CrestFloatingOrigin.cs", "Node3D", "CrestFloatingOrigin"),
        ("res://addons/crest/csharp/CrestShapeGerstner.cs", "Node3D", "CrestShapeGerstner"),
        ("res://addons/crest/csharp/CrestShapeFFT.cs", "Node3D", "CrestShapeFFT"),
        ("res://addons/crest/csharp/CrestSimpleFloatingObject.cs", "Node3D", "CrestSimpleFloatingObject"),
        ("res://addons/crest/csharp/CrestSphereWaterInteraction.cs", "Node3D", "CrestSphereWaterInteraction"),
        ("res://addons/crest/csharp/CrestBoatProbes.cs", "Node3D", "CrestBoatProbes"),
        ("res://addons/crest/csharp/CrestUnderwaterRenderer.cs", "Node3D", "CrestUnderwaterRenderer"),
        ("res://addons/crest/csharp/CrestOceanDepthCache.cs", "Node3D", "CrestOceanDepthCache"),
        ("res://addons/crest/csharp/CrestOceanPlanarReflection.cs", "Node3D", "CrestOceanPlanarReflection"),
        ("res://addons/crest/csharp/CrestWaterBody.cs", "Node3D", "CrestWaterBodyCs"),
        ("res://addons/crest/csharp/CrestShapeGerstner.cs", "Node3D", "CrestShapeGerstnerCs"),
        ("res://addons/crest/csharp/CrestShapeFFT.cs", "Node3D", "CrestShapeFFTCs"),
        ("res://addons/crest/csharp/CrestRegisterLodDataInput.cs", "MeshInstance3D", "CrestRegisterLodDataInput"),
        ("res://addons/crest/csharp/CrestRegisterFoamInput.cs", "MeshInstance3D", "CrestRegisterFoamInput"),
        ("res://addons/crest/csharp/CrestRegisterFlowInput.cs", "MeshInstance3D", "CrestRegisterFlowInput"),
        ("res://addons/crest/csharp/CrestRegisterSeaFloorDepthInput.cs", "MeshInstance3D", "CrestRegisterSeaFloorDepthInput"),
        ("res://addons/crest/csharp/CrestRegisterClipSurfaceInput.cs", "MeshInstance3D", "CrestRegisterClipSurfaceInput"),
        ("res://addons/crest/csharp/CrestRegisterAlbedoInput.cs", "MeshInstance3D", "CrestRegisterAlbedoInput"),
        ("res://addons/crest/csharp/CrestRegisterShadowInput.cs", "MeshInstance3D", "CrestRegisterShadowInput"),
        ("res://addons/crest/csharp/CrestRegisterAnimWavesInput.cs", "MeshInstance3D", "CrestRegisterAnimWavesInput"),
        ("res://addons/crest/csharp/CrestRegisterHeightInput.cs", "MeshInstance3D", "CrestRegisterHeightInput"),
        ("res://addons/crest/csharp/CrestRegisterDynWavesInput.cs", "MeshInstance3D", "CrestRegisterDynWavesInput"),
    };

    private Texture2D? _icon;

    public override void _EnterTree()
    {
        _icon = GD.Load<Texture2D>("res://addons/crest/icons/ocean.svg");
        foreach (var type in Types)
        {
            var script = GD.Load<Script>(type.ScriptPath);
            if (script != null)
                AddCustomType(type.Name, type.BaseType, script, _icon);
        }
    }

    public override void _ExitTree()
    {
        foreach (var type in Types)
            RemoveCustomType(type.Name);
        _icon = null;
    }
}
