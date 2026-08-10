# Changelog

All notable changes to Crest for Godot are documented here. Versions follow
[Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-08-10

First staged release of the Godot 4.6 C# port.

### Included

- View-following, crack-free LOD ocean renderer.
- GPU Gerstner and FFT wave generation.
- Animated waves, foam, dynamic waves, flow, sea-floor depth, shadow,
  clip-surface, and albedo simulations.
- Buoyancy, water queries, underwater rendering, and planar reflections.
- Demo scenes for the main showcase and Three Boats.

### Known limitations

- Requires Godot 4.6+ .NET/Mono and the Forward+ or Mobile renderer.
- Exact GPU readback collision queries for FFT waves are not implemented.
- Planar reflection clipping and the underwater meniscus are approximations.
- This is an early staged release; APIs and serialized resources may change
  before 1.0.

[0.1.0]: https://github.com/EthanCS/crest-godot/releases/tag/v0.1.0
