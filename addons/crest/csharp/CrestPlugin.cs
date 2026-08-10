using Godot;

namespace Crest.Godot;

/// <summary>
/// C# editor entry point. Runtime nodes remain script-compatible while their
/// registration is now owned by the .NET plugin assembly.
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
        ("res://addons/crest/csharp/CrestRegisterFoamInput.cs", "Node3D", "CrestRegisterFoamInput"),
        ("res://addons/crest/csharp/CrestRegisterFlowInput.cs", "Node3D", "CrestRegisterFlowInput"),
        ("res://addons/crest/csharp/CrestRegisterSeaFloorDepthInput.cs", "Node3D", "CrestRegisterSeaFloorDepthInput"),
        ("res://addons/crest/csharp/CrestRegisterClipSurfaceInput.cs", "Node3D", "CrestRegisterClipSurfaceInput"),
        ("res://addons/crest/csharp/CrestRegisterAlbedoInput.cs", "Node3D", "CrestRegisterAlbedoInput"),
        ("res://addons/crest/csharp/CrestRegisterShadowInput.cs", "Node3D", "CrestRegisterShadowInput"),
        ("res://addons/crest/csharp/CrestRegisterAnimWavesInput.cs", "Node3D", "CrestRegisterAnimWavesInput"),
        ("res://addons/crest/csharp/CrestWaterBody.cs", "Node3D", "CrestWaterBodyCs"),
        ("res://addons/crest/csharp/CrestShapeGerstner.cs", "Node3D", "CrestShapeGerstnerCs"),
        ("res://addons/crest/csharp/CrestShapeFFT.cs", "Node3D", "CrestShapeFFTCs"),
        ("res://addons/crest/csharp/CrestRegisterLodDataInput.cs", "Node3D", "CrestRegisterLodDataInputCs"),
        ("res://addons/crest/csharp/CrestRegisterFoamInput.cs", "Node3D", "CrestRegisterFoamInputCs"),
        ("res://addons/crest/csharp/CrestRegisterFlowInput.cs", "Node3D", "CrestRegisterFlowInputCs"),
        ("res://addons/crest/csharp/CrestRegisterSeaFloorDepthInput.cs", "Node3D", "CrestRegisterSeaFloorDepthInputCs"),
        ("res://addons/crest/csharp/CrestRegisterClipSurfaceInput.cs", "Node3D", "CrestRegisterClipSurfaceInputCs"),
        ("res://addons/crest/csharp/CrestRegisterAlbedoInput.cs", "Node3D", "CrestRegisterAlbedoInputCs"),
        ("res://addons/crest/csharp/CrestRegisterShadowInput.cs", "Node3D", "CrestRegisterShadowInputCs"),
        ("res://addons/crest/csharp/CrestRegisterAnimWavesInput.cs", "Node3D", "CrestRegisterAnimWavesInputCs"),
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
