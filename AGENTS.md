# Crest for Godot — developer notes

This repository is a port of the Crest ocean system (Unity, MIT, by Wave
Harmonic) to Godot 4.6. The original repository:
https://github.com/wave-harmonic/crest

## Layout

- `addons/crest/` — the plugin (everything ships here)
  - `core/` — ocean renderer (singleton), LOD mesh builder, lod transforms,
    RD compute helper, time provider, water body, floating origin, debug GUI
  - `shapes/` — wave spectrum resource, Gerstner + FFT generators
    (`fft_compute.gd` pipeline)
  - `simulation/` — per-sim managers (animated waves, foam, dynamic waves,
    sea floor depth, flow, shadow, clip surface, albedo) + `settings/`
    resources + `ocean_depth_cache.gd`
  - `shaders/` — `ocean.gdshader` (surface), `underwater.gdshader`,
    `sim/*.glsl` (compute), `include/*.gdshaderinc` (spatial includes)
  - `interaction/` — buoyancy + water interaction components
  - `collision/` — wave height/normal query API (analytic Gerstner mirror)
  - `inputs/` — RegisterXxxInput nodes injected into sims
  - `underwater/`, `reflection/` — underwater post-process, planar reflections
- `demo/` — demo scenes
- `_reference/` (git-ignored) — local clone of the Unity Crest repo for
  comparison, never shipped

## Hard-won platform conventions (follow them!)

- **Push constants are float-only.** GDScript `PackedFloat32Array` cannot
  carry ints; declare all push constant members `float` in GLSL and convert
  with `int()` on use. Always pack with
  `CrestRDCompute.pack_push_constants()` (pads to 16 bytes, required).
- **Uniform sets must not be freed immediately.** Use
  `CrestRDCompute.free_rid_deferred()`; the ocean flushes the queue each
  frame. Immediate `free_rid` corrupts in-flight GPU work (fences time out).
- **`RenderingDevice.texture_get_data` is unreliable on some drivers**
  (observed fence timeouts on macOS Metal). Verify GPU results visually via
  a `SubViewport` screenshot instead; main-viewport readback returns black
  in some environments, SubViewport readback works.
- **RD textures bridge to materials via `Texture2DArrayRD`/`Texture2DRD`,
  and only for spatial shaders.** canvas_item shaders cannot sample RD
  textures.
- **`Texture2DArrayRD` requires `array_layers > 1`.**
- **New `class_name` scripts require an editor rescan** before running:
  `Godot --headless --path . --import`.
- Compute `.glsl` files use `#[compute]` + `#version 450`; `#include` is
  resolved by `CrestRDCompute.from_file` (relative to the shader).
- Cascade data layout (SSBO + shader uniform arrays): 2 vec4s per cascade,
  16 entries (`CASCADE_PARAMS_COUNT`); see `lod_transform.gd`.
- **canvas_item shaders cannot use `hint_depth_texture`** (nor the
  projection matrix builtins). Full-screen post effects must be spatial
  full-screen quads with a clip-space `POSITION` override (see
  `underwater.gdshader` + `underwater_renderer.gd`).
- **Uniform defaults of runtime-created ShaderMaterials are unreliable**
  (observed zeros for unset uniforms). Sync every uniform explicitly each
  frame, like `ocean_renderer.gd::_sync_material_params` does.
- Multiple `get_image()` calls on the same SubViewport texture can return
  stale frames on some drivers — capture at most one screenshot per run.
- Freezing `CrestTimeProvider` (`use_custom_time`) stops the GPU wave
  update while the CPU collision mirror keeps evaluating the analytic
  waves — the two views of the surface diverge.
- `CrestWaveSpectrum` randomises component phases per run unless
  `random_seed` is set — screenshots are not reproducible across runs by
  default.
- **`Environment.AMBIENT_SOURCE_SKY` is 3, not 2** (2 = CANVAS/COLOR).
  A scene environment with the wrong source silently gives zero ambient
  light on the water (black water) — check the synced `ambient_light`
  uniform when debugging colour issues.
- Screen-space textures (`hint_screen_texture`/`hint_depth_texture`)
  behave differently between the root viewport and SubViewports
  (out-of-screen refraction UVs returned black in the root viewport) —
  clamp refraction UVs and cancel out-of-screen taps.

## Testing

Run the smoke test scene and inspect the screenshot it writes:

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot --path . \
  --quit-after 300 _tmp_test/ocean_smoke.tscn
# screenshot: ~/Library/Application Support/Godot/app_userdata/<project>/ocean_smoke.png
```
