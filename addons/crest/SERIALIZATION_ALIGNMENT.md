# Unity Crest serialization alignment

Baseline: Wave Harmonic Crest commit `db0658f` (2026-06-18), vendored locally at
`_reference/crest`. Field names in this document are Unity YAML keys, not
Inspector labels. Godot C# exports intentionally preserve the leading underscore
and camel-case spelling of those keys.

## Configuration assets

| Unity asset | Godot resource | Serialized fields |
| --- | --- | --- |
| `OceanWaveSpectrum` | `CrestWaveSpectrum` | `_version`, `_fetch`, `_waveDirectionVariance`, `_gravityScale`, `_smallWavelengthMultiplier`, `_multiplier`, `_powerLog`, `_powerDisabled`, `_chopScales`, `_gravityScales`, `_chop`, `_showAdvancedControls`, `_model` |
| `SimSettingsAnimatedWaves` | `CrestSimSettingsAnimatedWaves` | `_version`, `_waveResolutionMultiplier`, `_attenuationInShallows`, `_shallowsMaxDepth`, `_collisionSource`, `_maxQueryCount`, `_pingPongCombinePass`, `_renderTextureGraphicsFormat` |
| `SimSettingsSeaFloorDepth` | `CrestSimSettingsSeaFloorDepth` | `_allowVaryingWaterLevel` |
| `SimSettingsFoam` | `CrestSimSettingsFoam` | `_version`, `_prewarm`, `_foamFadeRate`, `_waveFoamStrength`, `_waveFoamCoverage`, `_filterWaves`, `_shorelineFoamMaxDepth`, `_shorelineFoamStrength`, `_renderTextureGraphicsFormat`, `_simulationFrequency` |
| `SimSettingsWave` | `CrestSimSettingsWave` | `_version`, `_minGridSize`, `_maxGridSize`, `_simulationFrequency`, `_damping`, `_courantNumber`, `_attenuationInShallows`, `_horizDisplace`, `_displaceClamp`, `_gravityMultiplier` |
| `SimSettingsFlow` | `CrestSimSettingsFlow` | `_version` |
| `SimSettingsShadow` | `CrestSimSettingsShadow` | `_version`, `_jitterDiameterSoft`, `_currentFrameWeightSoft`, `_jitterDiameterHard`, `_currentFrameWeightHard`, `_allowNullLight`, `_allowNoShadows` |
| `SimSettingsClipSurface` | `CrestSimSettingsClipSurface` | `_version`, `_renderTextureGraphicsFormat` |
| `SimSettingsAlbedo` | `CrestSimSettingsAlbedo` | `_version`, `_resolution` |

The Unity `GraphicsFormat` integer values are retained: `R8_UNorm = 21`,
`R16_SFloat = 45`, and `R16G16B16A16_SFloat = 48`.

## Runtime attachment model

The official Main example has these authored components:

| Unity GameObject | Unity component | Godot node/script |
| --- | --- | --- |
| `Ocean` | `OceanRenderer` | `CrestOceanRenderer` / `CrestOceanRendererFacade.cs` |
| child `Waves` | `ShapeFFT` | child `CrestShapeFFT` / `CrestShapeFFT.cs` |
| depth-cache object | `OceanDepthCache` | `CrestOceanDepthCache.cs` |
| camera | `UnderwaterRenderer`, optionally `OceanPlanarReflection` | camera children using `CrestUnderwaterRenderer.cs` and `CrestOceanPlanarReflection.cs` |
| optional water areas | `WaterBody` | `CrestWaterBody.cs` |
| optional interactions | `RegisterXxxInput`, `SphereWaterInteraction`, floating-object component | corresponding `CrestRegisterXxxInput.cs`, `CrestSphereWaterInteraction.cs`, and floating-object scripts |

Neither implementation authors simulation managers as scene components. Unity
`OceanRenderer` constructs `LodDataMgr*` objects; the Godot facade constructs the
corresponding C# managers and an internal `CrestOceanRendererBackend` child.

The runtime supports the official Main example's `ShapeFFT` attachment model.
The repository demo intentionally mounts `ShapeGerstner` to preserve the
`d32f43c` visual baseline (strong boat interaction, shallow seabed and visible
caustics); both generators use the official serialized field names. The
underwater renderer remains mounted under the camera.

### Renderer and Material-owned ocean inputs

`CrestRegisterXxxInput` nodes derive from `MeshInstance3D`. This is the Godot
equivalent of a Unity GameObject carrying both a `Renderer` and a
`RegisterXxxInput`: `Mesh` and `Transform3D` define the footprint, while a
`ShaderMaterial` serializes the official Crest shader property names.

The common component schema is split exactly like Crest:

| Official component level | Serialized fields |
| --- | --- |
| `RegisterLodDataInput` | `_checkShaderName`, `_checkShaderPasses`, `_disableRenderer` |
| `RegisterLodDataInputWithSplineSupport` | `_featherWidth`, `_overrideSplineSettings`, `_radius`, `_subdivisions` |
| `RegisterAnimWavesInput` | `_version`, `_filterByWavelength`, `_octaveWavelength`, `_renderAfterDynamicWaves`, `_followHorizontalMotion`, `_maxDisplacementVertical`, `_maxDisplacementHorizontal`, `_reportRendererBoundsToOceanSystem` |
| `RegisterDynWavesInput` | `_version` |
| `RegisterFoamInput` | `_version`, `_followHorizontalMotion` |
| `RegisterFlowInput` | `_version`, `_followHorizontalMotion` |
| `RegisterSeaFloorDepthInput` | `_version`, `_assignOceanDepthMaterial`, `_relative` |
| `RegisterHeightInput` | `_version`, `_debug` (`_drawBounds`) |
| `RegisterClipSurfaceInput` | `_version`, `_mode`, `_primitive`, `_order`, `_inverted`, `_disableClipSurfaceWhenTooFarFromSurface`, `_animatedWavesDisplacementSamplingIterations` |
| `RegisterAlbedoInput` | `_version`, `_followHorizontalMotion` |
| `RegisterShadowInput` | `_version` |

Spline fields are inherited only by Animated Waves, Foam, Flow, and Height.
They are intentionally absent from Clip, Albedo, Sea Floor Depth, Shadow, and
Dynamic Waves, matching the official class hierarchy.

| Crest input shader | Godot shader | Material fields |
| --- | --- | --- |
| Animated Waves/Add From Texture | `anim_waves_add_from_tex.gdshader` | `_MainTex`, `_Strength`, `_HeightsOnly` |
| Animated Waves/Set Water Height Using Geometry | `anim_waves_set_height.gdshader` | `_ColorWriteMask` (height comes from geometry world Y) |
| Foam/Add From Texture | `foam_add_from_tex.gdshader` | `_MainTex`, `_Strength` |
| Foam/Add From Vertex Color | `foam_add_from_vert_col.gdshader` | `_Strength` |
| Foam/Override | `foam_override.gdshader` | `_FoamValue` |
| Flow/Add Flow Map | `flow_add_flow_map.gdshader` | `_FlowMap`, `_Strength`, `_FlipX`, `_FlipZ`, `_FeatherAtUVExtents`, `_FeatherWidth` |
| Flow/Fixed Direction | `flow_fixed_direction.gdshader` | `_Speed`, `_Direction`, `_FeatherAtUVExtents`, `_FeatherWidth` |
| Dynamic Waves/Add Bump | `dynamic_waves_add_bump.gdshader` | `_Amplitude`, `_Radius` |
| Albedo/Color | `albedo_color.gdshader` | `_Texture`, `_Color`, `_Cutoff`, `_BlendModeSource`, `_BlendModeTarget` |
| Clip Surface/Remove Area Texture | `clip_surface_remove_area_texture.gdshader` | `_MainTex` |
| Depth/Ocean Depth From Geometry | `ocean_depths.gdshader` | no Material fields; world-space geometry height is the data |
| Depth/Cached Depths | `ocean_depths_cache.gdshader` | `_MainTex` (`_HeightOffset` is supplied by the cache/runtime draw path) |
| Shadow/Override | `shadow_override.gdshader` | `_ShadowValue` |

Mode is selected by the shader asset, as in Crest. There are deliberately no
serialized `SetHeight`, `SphereMode`, or `FixedDirection` switches. There are
also no invented component fields named `Amplitude`, `Strength`, `FlowMap`,
`DirectionDegrees`, `HeightTexture`, `HeightOffset`, `Tint`, `MaskTexture`, or
`Radius`. In particular, Crest's `_Direction` is a normalized turn in `[0, 1]`,
not degrees. Baked height textures remain owned by `OceanDepthCache._savedCache`.

At runtime the port projects the real Mesh triangles into the simulation,
including Transform3D, UVs, vertex colours, texture `_ST`, alpha cutoff, every
surface/submesh and its corresponding ShaderMaterial. The raster textures and
manager dictionaries are transient and are never serialized. Primitive Clip
Sphere/Cube inputs use `_primitive`, apply `_order`, `_inverted`, displaced-wave
sampling iterations and the vertical-distance disable option directly in the
compute pass. `_followHorizontalMotion`, animated-wave wavelength placement,
before/after Dynamic Waves placement, and reported displacement cull margins
also participate in runtime scheduling rather than existing as data-only
compatibility fields.

There are no Godot-only serialized control parameters on an official-mapped
`CrestRegisterXxxInput`. Godot's built-in `Mesh`, `MaterialOverride`, and
`Transform3D` are the engine equivalents of Unity's Renderer/MeshFilter,
materials, and Transform; they are not replacement Crest settings.

## Porting rules

- A scene-facing `[Export]` key must be an official Crest serialized field name.
- Godot-only transient controls must not be serialized on official-mapped components.
- Unity material-backed input controls are serialized on Godot `ShaderMaterial`
  resources using the exact official shader property name, never as invented
  `RegisterXxxInput` component fields.
- Engine object types are translated (`Material` to `ShaderMaterial`, `Camera`
  to `Camera3D`, and so on), but the owning field name and default value remain
  aligned where the concepts correspond.
