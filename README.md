# OMSI-Compatible-Runtime

Independent Windows x64 runtime intended to load compatible OMSI content without using `Omsi.exe`.

> Early research/development project. This repository does not include or redistribute proprietary OMSI binaries, DRM components, or game assets.

## Current multiplayer/runtime research

The DRAFT runtime branch also includes experimental openOMSI-compatible LAN protocol v6 work: session invite codes, shared AI traffic/light states, on-foot `Walker` synchronization, and decoding of openOMSI `WORLD` people/passenger states. The on-foot controls are `Ctrl+Shift+G` to leave the bus, `W/A/S/D` to walk, `Shift` to run, `Space` to jump, `C` to crouch, and `G` near the bus to return.

## Initial goal

The first milestone is a native x64 **World Runtime** capable of:

- locating a user-selected OMSI content directory;
- discovering installed maps;
- parsing the initial map configuration;
- building a normalized internal world model;
- loading world assets incrementally;
- reporting missing/unsupported content with detailed diagnostics;
- running without launching `Omsi.exe`.

## Architecture

- **Launcher** — x64 bootstrap/configuration and content selection.
- **Runtime** — x64 game process.
- **OmsiCompat** — clean-room compatibility parsers and normalized models.
- **Diagnostics** — structured logs and compatibility reports.

## Compatibility approach

The project implements its own runtime and parsers. Proprietary game code is not copied into this repository. Users are expected to provide content they are legally entitled to use.

## Status

Pre-alpha bootstrap.
