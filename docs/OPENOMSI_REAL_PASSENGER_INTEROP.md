# Real openOMSI ↔ Runtime passenger interoperability

This is a **real-peer acceptance procedure**, not a Runtime↔Runtime smoke test.
Use the current **openOMSI protocol v6** release (game host or dedicated server)
and a build of the Runtime branch under test, with a legally installed original
OMSI 2 content root. Do not use fake UDP peers, generated passenger fixtures
or simulated claims to mark this procedure as passed.

## Required setups

1. **openOMSI hosts; Runtime joins.** The host runs a real map with timetable
   stops, human assets and passengers enabled. Both installations have the same
   map and bus add-ons. Use the openOMSI session code or the host's LAN IP:port
   (default UDP port 27015). A dedicated openOMSI server is also acceptable.
2. **Runtime hosts; openOMSI joins.** Use the same map, bus and timetable.
   Record both programs' versions and the Runtime commit SHA.
3. Perform each direction once with two client observers, if available, to
   verify that passenger visibility is independent for each observer.

## Real-peer acceptance checks

- **Handshake and WORLD:** verify protocol version, host/client connection,
  receiving WORLD people frames and DESC human paths, and real .hum models
  appearing at the correct timetable stops (not merely codec unit tests).
- **CLAIM / GRANT:** stop a compatible bus within boarding range, open its
  passenger-entry doors, and check the passenger ID sent in CLAIM and returned
  in GRANT. A waiting passenger must disappear from the host's waiting area
  and appear **once** walking through the real entry path in the claimant's bus.
  Verify the seated place and the .hum animation.
- **DENY / ownership race:** let two actual clients try to board the same
  waiting passenger. Only one may own the ID. The DENY client must not spawn
  that person. In packet traces, include both `GONE → GRANT` and
  `GRANT → GONE` delivery orders, and late `DESC` arrival if observed.
  Both orders must lead to the same single owner.
- **Uplink and observation:** while boarded, check the seated passenger
  locally, on the openOMSI host and on another real peer. On exit, verify
  position/animation, disappearance (GONE), and no stuck riders.
- **View boundaries:** drive across the 260 m person visibility radius,
  then return. Nearby WORLD people must disappear and reappear exactly once;
  no permanent ghost, missing .hum, stale seated rider or duplicated ID.
- **Reconnect:** interrupt the client network briefly and reconnect to the
  *same* openOMSI session within the supported reconnect window. Re-check
  existing passengers and their authority; previously claimed people must
  not respawn at the original stop. Repeat with a fully closed session and
  a new host session: old WORLD descriptions/claims must not leak.
- **Timeout and late packets:** repeat with packet loss/reordering on the
  actual network connection and check that late GRANT, GONE, DESC, timeout,
  and DENY do not duplicate or silently drop a claimed passenger.

## Evidence and pass/fail reporting

Record for **each direction**: Runtime SHA, openOMSI release/commit,
map, bus, relevant people IDs, player IDs, stop ID, timestamps,
connection/drop/reconnect time, and the openOMSI and Runtime logs.
Retain packet captures or logs showing WORLD/DESC/CLAIM/GRANT/DENY/GONE
ordering. Keep secrets/session tokens out of shared logs.

Runtime emits `[multiplayer-world]` claim request/result messages; compare
them against the actual openOMSI host logs and on-screen passenger state.
A successful CI build (including Runtime↔Runtime smoke tests) is **not**
evidence of a passed real-peer test. Mark individual checks `PASS`,
`FAIL` or `NOT RUN` with the supporting log/time; do not report
the overall scenario as passed until both real peers have been exercised.

## Reference

openOMSI protocol/world ownership and host/client behavior:
- https://github.com/openOMSI-Project/openOMSI/blob/main/docs/USER_GUIDE.md
- https://github.com/openOMSI-Project/openOMSI/blob/main/crates/omsi-net/src/world.rs
- https://github.com/openOMSI-Project/openOMSI/blob/main/crates/omsi-app/src/lan_world.rs
