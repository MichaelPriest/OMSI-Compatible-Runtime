# Architecture

## Product boundary

This repository is the game/runtime only. It does not contain the OMSI Map Studio editor and does not depend on the NavBR Multiplayer project.

## Execution model

```text
Launcher WinUI 3 (C#) / legacy launcher (C#)
           |
           v
Standalone runtime x64 (C# host + D3D11)
           |
           +-- OmsiCompat managed compatibility layer (staged fallback)
           |
           +-- omsi_compat_core.dll (Rust x64, stable C ABI v1)
           |     OMSI texture and add-on path lookup (live)
           |
           v
User-provided compatible OMSI 2 content
```

The native Rust library is built with Cargo on Windows x64 and packaged
beside both launchers and the runtime executable. C# first invokes the
versioned native texture resolver; unresolved paths still fall through to the
existing managed implementation until real content validates parity.
The Windows CI requires Rust DLL loading and a minimum of three successful
native texture resolutions during smoke tests.

The x86 OMSI plugin host remains an independent x86 process. Never load
the Rust x64 DLL into that host. Physics/ODE, scripts, and rendering are not
replaced by placeholders during this migration.

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


## Rust migration plan (incremental, no main merge)

1. **Texture/content path lookup**: live Rust native x64 core + FFI and regression
   tests; retained managed fallback for add-on equivalence.
2. **Content parsers**: compare O3D, maps and splines against stock and add-on
   data, moving one format at a time behind explicit compatibility tests.
3. **Script VM and dynamics**: prove OMSI script variables, engine torque,
   gearbox and braking traces match real buses before replacing C# paths.
4. **Rendering**: evaluate a wgpu renderer separately, keeping D3D11 active
   until transparent panels, scenery, mirrors and frame diagnostics agree.
5. **UI and plugins**: retain WinUI 3; keep OMSI plugin host x86 isolated.

openOMSI (MIT) is the implementation reference. neoOMSI (GPL-3.0-or-later)
may inform comparison tests and ideas; no neoOMSI GPL implementation is copied
into the existing runtime. The repository must keep copyright notices for
any third-party source incorporated in future phases.
