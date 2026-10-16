# Rust + openOMSI migration plan

## Decision

Continue the existing `OMSI-Compatible-Runtime` branch with Rust as the compatibility-core language and use the public `openOMSI-org/openOMSI` project as the primary behavioral and format reference. This is an incremental migration, not a wholesale replacement of the runtime.

- Working branch: `fix/runtime-visual-controls-pass`
- Pull request: #2, keep DRAFT
- Baseline inspected for this plan: `47c0aa33d602ccce47873db39b9e54410b9f8be7`
- Never push this work to `main`, merge PR #2, or replace existing runtime systems without explicit authorization.

## Preserve during migration

- Existing Windows x64 launcher and current WinUI 3 workflow.
- Existing D3D11 renderer until a tested alternative proves parity.
- Existing ODE physics integration and OMSI x86 plugin host.
- Existing C# runtime and parsers as working fallbacks while each Rust subsystem is validated.
- Current real-content and smoke tests. Do not add fake compatibility claims, replace real-asset checks with fixtures, or weaken existing tests.

## Why incremental integration

The repository already has a Rust `cdylib` at `native/omsi-compat-core`, a versioned C ABI, a C# P/Invoke bridge, CI unit tests, and packaging of `omsi_compat_core.dll`. The first Rust subsystem is texture-path resolution. Build on that integration instead of introducing a second competing runtime or copying a large upstream tree before compatibility and licensing boundaries are reviewed.

Upstream reference: https://github.com/openOMSI-org/openOMSI

openOMSI is a from-scratch Rust recreation and is still marked early-release by its maintainers. It requires a legally installed original OMSI 2 content directory and does not ship game assets. Treat its implementation and documented behavior as a reference to verify, not as proof that every mod works in this runtime.

## Migration order

### 0. Baseline and evidence

1. Record the branch HEAD and CI result before every code batch.
2. Keep the existing Windows x64 build, Rust tests, synthetic-world smoke test, guidance tests, launcher startup smoke test, and real-content acceptance procedures intact.
3. Maintain a compatibility matrix for each selected real map/bus/add-on. Record exact content version, tested capability, logs, and PASS/FAIL/NOT RUN; never mark untested formats as supported.

### 1. Rust core hardening (current subsystem)

1. Keep the C ABI versioned and panic-safe; validate null pointers, UTF-8, output capacity, and path containment.
2. Exercise texture resolution against real installed OMSI assets as well as adversarial path cases.
3. Verify the C# bridge calls the native resolver when the DLL is present and uses the existing managed resolver only as the explicit fallback.
4. Preserve the CI packaging checks that require the native DLL in release output.

### 2. Content and map readers

Port one narrow parser at a time, starting with deterministic text formats and normalized output models. Compare results against the current C# reader on the same real files. Do not change the renderer or physics contract in the same patch.

Candidate order:
1. map/config and content-root metadata;
2. spline and scenery definitions;
3. model/O3D metadata and texture/material references;
4. timetable, HOF, and route data;
5. vehicle configuration and script semantics.

For each parser, add differential tests that compare Rust output with the current implementation, malformed-input tests, and real-content acceptance evidence before switching production calls.

### 3. Simulation and rendering boundaries

Keep the existing D3D11 renderer and ODE physics as the host while the Rust core takes over verified compatibility tasks. Establish explicit data contracts for transforms, units, coordinate axes, vehicle state, script variables, and diagnostics. A renderer migration or new graphics backend is a separate decision after parity and performance profiling.

### 4. OMSI script and vehicle behavior

Port RPN/script semantics and vehicle configuration incrementally. Preserve native OMSI behavior including throttle/brake/velocity, wheel variables, transmission, electrical startup, sounds, and add-on-specific triggers. Tests must cover real scripts and known edge cases; never substitute mocked vehicle state for compatibility evidence.

### 5. Acceptance gates

A subsystem is eligible to become the default only when:
- Rust unit tests and the full existing CI pass;
- C# interop and package staging are verified;
- differential results match the existing reader on selected real content;
- no previously passing smoke test was removed or relaxed;
- failures are reported as unsupported rather than silently ignored;
- real map/bus validation is recorded separately from CI success.

## Immediate next implementation slice

Audit and extend the existing Rust texture resolver's real-content test coverage, then select one small map/config parser for a Rust implementation with a versioned C ABI and differential comparison against the C# parser. Keep this work on `fix/runtime-visual-controls-pass` and leave PR #2 in DRAFT until real-content validation supports a stronger claim.
