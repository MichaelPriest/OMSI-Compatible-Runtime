# Architecture

## Product boundary

This repository is the game/runtime only. It does not contain the OMSI Map Studio editor and does not depend on the NavBR Multiplayer project.

## Execution model

```text
Launcher x64
    |
    v
Runtime x64
    |
    v
OmsiCompat
    |
    v
User-provided compatible content
```

The runtime does not require `Omsi.exe` to execute.

## Initial modules

### OmsiCompat.Core

Owns content-root discovery and common normalized definitions.

### OmsiCompat.Map

Owns map discovery and parsers. OMSI-specific syntax must be converted to normalized runtime models before reaching rendering/physics systems.

### OMSICompatible.Runtime

The standalone x64 game host. The first milestone is a World Runtime capable of loading and navigating a map.

### OMSICompatible.Launcher

The x64 startup/configuration application. The current bootstrap is intentionally minimal.

## Planned runtime sequence

1. Resolve content root.
2. Discover maps.
3. Parse `global.cfg`.
4. Parse tiles.
5. Parse terrain.
6. Resolve splines and crossings.
7. Resolve scenery objects.
8. Decode O3D geometry.
9. Resolve materials/textures.
10. Submit normalized scene data to the D3D11 renderer.
11. Stream nearby tiles asynchronously.

## Compatibility and legal boundary

This project uses an independent compatibility implementation. Proprietary binaries, assets, DRM components, or copied game code must not be committed to the repository.


## Compatibility-first x64 goal

The product goal is not to redesign OMSI gameplay. The runtime should preserve the user-facing behavior of OMSI content while replacing the legacy 32-bit execution layer with a modern 64-bit runtime.

Compatibility-facing behavior includes:

- OMSI keyboard/control semantics;
- vehicle electrical, engine, gearbox and brake state;
- map, spline, scenery, O3D, HOF and script behavior;
- existing add-on content expectations.

The x64 runtime is free to modernize the implementation underneath that compatibility surface:

- 64-bit address space;
- multi-threaded simulation work;
- asynchronous asset and tile streaming;
- modern D3D11/D3D12 rendering;
- GPU instancing, culling and batching;
- texture streaming and modern cache management;
- parallel content parsing and background preparation;
- modern audio/input backends.

The guiding rule is: **preserve observable OMSI behavior where compatibility matters; optimize the implementation behind it.**
