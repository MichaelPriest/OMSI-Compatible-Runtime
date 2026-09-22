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
