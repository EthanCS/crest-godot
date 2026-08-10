# Crest Godot C# Migration and Unity Comparison

## Current state

- Target: Godot 4.6.2 Mono, Forward+ or Mobile renderer.
- Plugin runtime/editor code: 50 C# files under `addons/crest/csharp/`.
- Plugin GDScript count: zero. Obsolete `.gd.uid` files were also removed.
- GPU simulations remain GLSL compute shaders and the surface/post effects
  remain Godot spatial shaders.
- `CrestPlugin.cs` is the editor entry point and all primary custom node types
  resolve to C# scripts.

The scene-facing `CrestOceanRendererFacade` owns configuration, C# time,
generated patch meshes/chunks, simulation managers and teardown. Its typed
`CrestOceanRendererBackend` owns cascade buffers, viewer-following scale/LOD
updates, simulation scheduling, lighting/material synchronization, fallback
textures and deferred GPU resource release.

When no material is assigned, the facade creates the bundled ocean material
before generating chunks. Runtime changes to LOD geometry or simulation-enable
properties rebuild the backend and preserve registered shape/input children.
All foam, dynamic-wave, flow, shadow, clip and albedo settings resources are
forwarded to their managers. A global `CrestTimeProviderCs` overrides the local
clock when installed.

The production update order matches Crest:

1. Sea-floor depth
2. Flow
3. Dynamic waves
4. Animated waves
5. Foam
6. Shadow
7. Clip surface
8. Albedo

## Ported systems

- LOD transform, concentric patch builder, chunk setup, skirts and bounds
- RenderingDevice compute helper, include expansion and float-only packed push
  constants
- Deferred RID/uniform-set cleanup with cached-set validity handling
- Gerstner spectrum generation, GPU evaluation and analytic CPU collision
- Live FFT spectrum update and separable radix-2 inverse transform
- Animated waves, foam, dynamic waves, depth, flow, shadow, clip and albedo
  cascade managers
- All simulation input node contracts
- Static and captured sea-floor depth cache
- Water-body AABB registry
- Underwater fog/meniscus post effect
- Mirrored-camera planar reflections
- Simple and probe-based buoyancy plus sphere water interaction
- Floating origin, time provider, debug texture GUI and typed simulation settings
- Sampling helpers for height, normal and vertical velocity
- Runtime renderer rebuilds and the shared floating-object base contract

`CrestOceanDepthCache` supports a supplied RF heightmap and one-shot/realtime
top-down scene capture. Captured height is packed through RGB to survive the
viewport's clamped sRGB intermediate, decoded to RF on the CPU, and uploaded
copies are invalidated safely before recapture.

## Verification

`dotnet build Crest.sln` completes with zero warnings and zero errors. A Godot
editor rescan completes without import issues after removal of every plugin
GDScript file.

The following Vulkan Forward+ runs exit with zero errors or warnings:

- `_tmp_test/csharp_core_test.tscn`
- `_tmp_test/csharp_code_created_ocean_test.tscn`
- `demo/main.tscn`
- `_tmp_test/csharp_fft_shape_test.tscn`
- `_tmp_test/csharp_underwater_test.tscn`
- `_tmp_test/csharp_reflection_test.tscn`
- `_tmp_test/csharp_interaction_test.tscn`
- `_tmp_test/csharp_depth_cache_test.tscn`

The core test covers cascade math, mesh topology, RD allocation/swap, settings,
Gerstner dirty/version and single-weight CPU behavior, floating-object
inheritance, all input groups, simulation-manager allocation, FFT initialization
and floating-origin shifts. The code-created ocean harness leaves the material
unset, verifies 28 generated chunks and every settings object, uses a global
custom time of 42, then changes LOD count and disables shadows at runtime; it
verifies the rebuilt backend has 40 chunks and a valid replacement cascade
buffer. The interaction harness verifies real dynamic-wave sphere dispatch and
scene settings. The depth-cache harness captures a plane at a known world Y and
checks the decoded RF height.

The FFT harness no longer treats a clean process exit as success. It asserts
that `CrestFFTComputeCs.SampleDispatchCount` advances; the current run produced
11 final FFT-to-animated-wave dispatches. Optional flow, shadow, clip and albedo
managers are enabled together in the code-created harness, which also caught and
fixed the shadow shader's non-float push-constant layout.

Current FFT visual evidence is
`_tmp_test/csharp_audit_fft00000010.png`. It shows a nonblank ocean surface,
depth/refraction colour, large floating body and boat while the HUD reports
scale 8 and advancing C# time. The dedicated FFT dispatch assertion ran during
the same 12-frame capture. The serialized demo Gerstner node currently has
`weight = 0.0`, so the normal demo is intentionally not used as Gerstner-wave
evidence.

## Unity Crest comparison

The checked-in Unity reference's core `Scripts` directory contains 115 C#
files, 105 of which are outside its `Editor` directory. The Godot port follows
the same LOD and simulation architecture, but engine APIs and current scope
produce these deliberate differences:

| Area | Godot C# port | Unity Crest difference |
| --- | --- | --- |
| Surface LOD | Concentric rings, snapping, morphing and skirts | Same architecture; Godot mesh/material APIs |
| Gerstner waves | GPU surface plus exact analytic CPU mirror | No spline-driven Gerstner variant |
| FFT waves | Live GPU FFT displacement | No baked FFT asset/baker or exact readback queries |
| Collision | Height/normal/velocity from Gerstner | No `QueryBase`, displacement/flow readback queue, or baked provider |
| Foam/dynamic/flow/depth | RenderingDevice cascade simulations | Same simulation roles, different command API |
| Shadows | Analytic registered sphere casters with jitter/EMA | Godot does not expose the main-light shadow map to RD |
| Underwater | Spatial full-screen fog and analytic meniscus | No ocean-mask prepass |
| Planar reflection | Mirrored camera plus horizontal correction | No exact oblique near-plane projection |
| Water bodies | AABB registry/containment | No spline/provider volumes |
| Time | Local pause, scale and custom clock | No network or cutscene provider implementations |
| Interaction | Buoyancy, probes and sphere forcing | No generic object-water adaptor contract |
| Inputs | Texture/rectangle inputs plus sphere forcing | No Unity generic dynamic-wave or height-input components |
| Diagnostics | Debug texture GUI and QA scenes | No collision ray-trace visualizers, alpha/wireframe helpers or Unity validation layer |
| Integrations | Standard Godot cameras and physics | No Unity XR helpers or Gaia integration |
| Tooling | Godot custom types and resources | No Unity inspectors, validators, build processors or baker UI |

## Missing or partial Unity functionality

1. Baked FFT data, baker/preview and exact FFT collision provider.
2. GPU displacement and flow query/readback APIs equivalent to `QueryBase`,
   `QueryDisplacements` and `QueryFlow`.
3. Spline authoring, spline-driven Gerstner waves, flow and foam.
4. Spline/provider water-body volumes and `FlowProvider` variants.
5. Networked and cutscene time providers.
6. Generic object-water interaction adaptor.
7. Unity's generic registered dynamic-wave/height inputs beyond the Godot
   texture/rectangle and sphere interaction paths.
8. Collision ray-trace helpers and visualizers, render-alpha/wireframe debug
   helpers, and the broader Unity validation framework.
9. XR-specific camera helpers and the Gaia integration.
10. Unity's underwater mask prepass, main-light shadow-map integration and exact
   oblique reflection clipping, which require different Godot renderer hooks.
11. Unity-specific editor/build tooling. These are not runtime blockers, but
   equivalent Godot authoring tools would still be useful.

The C# conversion is complete for the existing Godot plugin feature set. The
items above are upstream feature additions or engine-specific parity work, not
remaining GDScript migration tasks.
