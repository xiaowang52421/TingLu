# UPGRADE_05

status: READY_FOR_UNITY
role/model/effort: 05 / requested gpt-6-luna / xhigh; actual provider/model metadata is not available in this execution context
contract_version: 1.0.0

## Completed

- Added `StreetMemory : IStreetMemory` in `Assets/TingLu/Memory/StreetMemory.cs`.
- Added deterministic Role05 NUnit coverage in `Assets/TingLu/Tests/Upgrade/Role05/StreetMemoryTests.cs`.
- Added Unity `.meta` files for both C# files.
- Kept all writes within the assigned Memory, Role05 tests, and handoff paths. No shared contract, config, asmdef, fixture, Bootstrap, original project, or save files were changed.

## Behavior

- Permanent surface knowledge is monotonic: a new permitted surface polygon locally replaces any overlapping known material and refreshes its time; an empty surface result never deletes known ground. Disjoint convex fragments stay disjoint, and adjacent fragments with identical material and timestamps are merged when their union remains convex. Bounding boxes skip polygon clipping for distant fragments.
- Temporary contours are subtracted only by actual coverage; permanent contours remain known through empty re-probes and refresh only where a same-ID permitted contour overlaps. Empty Contact coverage cannot clear surrounding old fragments. Geometry is value-copied and does not track world objects.
- Same-ID observations at a new position retain old fragments outside coverage. Active geometry is rejected by the existing `StreetKnownObject` contract and is never read from acoustic observations.
- `MarkStale` and stale import preserve geometry while setting every surface/object `CurrentUntil` to zero. Export returns a fresh frame of immutable DTOs.

## Tests

Added tests for stable continuous area union and refresh, adjacent convex-fragment merging, temporary local difference, same-ID movement and later local removal, empty Contact coverage, stale restore, immutable export, and Active rejection.

Tests and Unity compilation are **NOT_RUN** by this role; 00 owns the Unity validation window. No runtime performance measurement was run. Geometry updates prefilter by coverage/piece bounds, but large or highly overlapping maps still need centralized profiling.

## Dependencies and next steps

- Uses only `TingLu.Contracts` and `StreetGeometry`; current Memory and Role05 asmdefs already reference required assemblies.
- 04 probe/contact behavior is consumed through `StreetProbeResult`; 04's broader test timeout remains a separate unresolved verification item.
- Next: 00 imports this source, compiles the Role05 assembly, runs `StreetMemoryTests`, and integrates `Current`/`Export`/`Import` with renderer and save service. Report any geometric or timing discrepancy before enabling full integration.

contract_requests: none
