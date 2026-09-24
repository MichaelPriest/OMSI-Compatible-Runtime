# Compatibility reference sources

This runtime is a clean-room compatibility implementation. Format and behavior
decisions should be grounded in documented or independently observable OMSI
behavior instead of visual guesswork.

## Source priority

1. Official OMSI documentation and SDK/manuals.
2. Observable behavior of the original OMSI Map Editor/runtime.
3. OMSI Map Studio code that has already been validated against real maps.
4. Independent open-source implementations and community documentation as
   cross-checks.
5. Heuristics only when the sources above do not cover the behavior.

## Current references

- Official OMSI SDK / Map Editor manual:
  https://www.omnibussimulator.de/download/SDK/Manual_SDK_1_EN.pdf
- OMSI WebDisk SDK index:
  https://reboot.omsi-webdisk.de/community/thread/460-doku-sdk-sdk-tools-din-fonts/
- OMSI WebDisk spline documentation:
  https://reboot.omsi-webdisk.de/wiki/entry/33-spline-files-sli/
- OMSI WebDisk scenery-object documentation:
  https://reboot.omsi-webdisk.de/wiki/entry/8-objekt/
- O3DParse / O3DView:
  https://github.com/space928/O3DView

O3DParse is GPL-3.0-or-later. It may be used as a behavioral/reference
validator, but must not become a runtime dependency without an explicit
licensing decision for this repository.

## Coordinate-space contract

Raw OMSI map placement coordinates use X/Y on the map plane and Z for height.

The World layer normalizes placements to renderer coordinates:

- World X = OMSI X
- World Y = OMSI Z (height)
- World Z = OMSI Y

Renderer-facing types must already be normalized. The renderer must never
repeat the raw OMSI X/Y/Z conversion.

Model geometry from the current O3D/.x loaders is renderer Y-up. Model vertex
positions must not receive an additional Y/Z swap in the renderer.

Every future coordinate-system change must include a regression test covering
the raw-placement -> normalized-world -> renderer boundary.
