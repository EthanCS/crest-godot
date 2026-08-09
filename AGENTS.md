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
- **Textures used by 3D shaders need mipmaps.** Godot's default texture
  import sets `mipmaps/generate=false`; without mips, `textureLod` /
  auto-mip fall back to mip 0 and distant water degrades into per-pixel
  speckle (caustics especially). The crest textures
  (`addons/crest/textures/*.png`) all have mipmaps enabled — keep it that
  way when adding textures.
- **Fragment sampling of imported textures should use auto-mip
  `texture()`** (Crest's `TiledTexture.Sample`), not `textureLod(uv, 0)`;
  RD sim textures (`ld_*`) are mip-less and stay on `textureLod(uv, 0)`
  like Crest's `SampleLevel`.
- **`ShaderMaterial.set_shader_parameter` from outside the ocean
  renderer's per-frame sync path is unreliable** (value reads back set but
  never reaches the render). Use the `CREST_MAT_OVERRIDES="key=val,..."`
  env hook in `ocean_renderer.gd::_sync_material_params` for A/B tests.
- **SubViewport QA shots freeze material uniform updates.** In a
  SubViewport (the metric_check harness), `set_shader_parameter` calls
  made after the material's first render never reach the GPU (floats,
  vectors, arrays, instance uniforms alike — verified with a minimal
  repro; the root viewport tracks updates fine). Per-frame synced values
  (crest_time, cascade data, force_underwater, ...) are stuck at their
  first-rendered value in harness shots. Consequences: QA of
  uniform-driven changes must set them via `CREST_MAT_OVERRIDES` (present
  from frame 0, so they land) or verify in the real window via
  `screencapture`; the wave field still moves because the sims run on the
  RenderingDevice, whose textures update regardless.
- **Do not use fragment-side `-VERTEX.z` as the surface view depth.**
  With displaced positions + the clip-space `POSITION` override it returns
  wrong values at grazing angles on some drivers (observed ~1.7x the true
  depth on Metal at ~600m), which zeroes the alpha fade and opens seabed
  holes in a regular slit pattern. The ocean shader computes the surface
  depth from the world position in the vertex pass (`v_smooth_z`) and uses
  it for fog, refraction scaling, caustics and the alpha fade.
- **Camera forward from `VIEW_MATRIX` is ROW 2 negated, not column 2.**
  `-VIEW_MATRIX[2].xyz` reads column 2 of the world→view matrix, which
  flips the pitch of the forward vector (only correct for a level camera);
  at downward view angles it fired the reconstructed refracted scene point
  dozens of metres into the sky and killed the caustics (invisible except
  stray patches). Use
  `-vec3(VIEW_MATRIX[0][2], VIEW_MATRIX[1][2], VIEW_MATRIX[2][2])`.

## Testing

Run the smoke test scene and inspect the screenshot it writes:

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot --path . \
  --quit-after 300 _tmp_test/ocean_smoke.tscn
# screenshot: ~/Library/Application Support/Godot/app_userdata/<project>/ocean_smoke.png
```

Quantitative visual QA harness (`_tmp_test/metric_check.tscn`): runs the
full demo in a SubViewport, takes ONE screenshot at `--shot-frame=N`,
counts direct-sand pixels (mesh gaps / over-displaced troughs expose the
bright seabed) and saves `user://metric_<tag>.png`. Useful args:
`--cam=x,y,z,pitch`, `--seed=N`, `--freeze=T`, `--no-boat`, `--mult=X`.
Compare shots with `_tmp_test/img_metric.py` (`diff` = min-shift mean|dL|
for LOD-transition popping, `grid` = high-pass speckle energy for
far-field gridding). Example:

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot --path . \
  --quit-after 400 _tmp_test/metric_check.tscn -- \
  --shot-frame=300 --no-boat --seed=7 --freeze=5 --cam=0,80,40,-60 --tag=chk
```

Other QA scenes:

- `_tmp_test/vert_check.tscn` — GPU-free numerical verification of the
  vertex pipeline: builds the real patch meshes, replicates the vertex
  shader math on the CPU and asserts every patch-border vertex welds to a
  neighbour (also UV round-trips). Run it after touching
  `ocean_builder.gd` or the vertex shader snap/morph code.
- `_tmp_test/crack_check.tscn` — crack reproduction: `--rise` (ascent,
  captures the exact scale-transition frame and checks
  tiles-vs-cascades scale consistency), `--orbit/--rotate/--static-yaw=D`
  with `--pos=x,y,z --pitch=D` (arbitrary camera poses).
- `debug_view` material uniform (set via `CREST_MAT_OVERRIDES`):
  1=foam amount, 2=foam texture, 3=foam rgb, 4=foam parts,
  5=refracted scene colour, **6=slice tint** (cascade weights — ring
  boundaries must crossfade smoothly), 7=sea depth/10, 8=surface Y,
  9=refraction fog/10, 10=surface view depth/200, 11=refracted scene
  depth/200, 12=(scene-surface depth)/20, 13=scene depth/200,
  14=surface view depth/1000, 15=scene depth/1000, 16=alpha fade,
  17=raw depth buffer. `force_opaque=1` disables the alpha fade.
