class_name CrestConstants
extends RefCounted
## Shared constants mirroring Crest's Constants.cs / LodDataMgr / OceanConstants.hlsl.

## Maximum number of LOD cascades supported (Crest: LodDataMgr.MAX_LOD_COUNT).
const MAX_LOD_COUNT := 15

## Cascade params array length: an extra duplicate entry lets shaders sample
## slice+1 unconditionally (Crest: LodTransform.WriteCascadeParams).
const CASCADE_PARAMS_COUNT := MAX_LOD_COUNT + 1

## Default LOD data texture resolution. Crest ships 384; we default to 256
## because the cascade band edges then align exactly with the FFT cascade
## bands (maxWavelength = 0.5 * 2^i), and it is one of Crest's recommended
## values.
const DEFAULT_LOD_DATA_RESOLUTION := 256

## Default LOD/cascade count (Crest default 7).
const DEFAULT_LOD_COUNT := 7

## Gravity used by wave dispersion (Crest: OceanRenderer.Gravity default).
const GRAVITY := 9.81

## Thread group size for compute dispatches (Crest: LodDataMgr.THREAD_GROUP_SIZE).
const THREAD_GROUP_SIZE := 8

## SSS remap constants (Crest: OceanConstants.hlsl).
const SSS_MAXIMUM := 0.6
const SSS_RANGE := 0.12

## Sea floor depth baseline: cleared value meaning "no terrain".
const OCEAN_DEPTH_BASELINE := -100000.0

## Underwater mask values (Crest: OceanConstants.hlsl).
const MASK_ABOVE_SURFACE := 1.0
const MASK_BELOW_SURFACE := -1.0
const MASK_BELOW_SURFACE_CULLED := -2.0
