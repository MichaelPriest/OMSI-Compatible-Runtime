# OMSI-Compatible-Runtime

Independent Windows x64 runtime intended to load compatible OMSI content without using `Omsi.exe`.

> Early research/development project. This repository does not include or redistribute proprietary OMSI binaries, DRM components, or game assets.

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
