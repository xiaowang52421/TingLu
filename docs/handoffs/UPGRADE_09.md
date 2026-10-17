# UPGRADE_09 交接

status: READY_FOR_UNITY (Role09 sources/resources; not an integrated playable build)  
role/model/effort: Role09；目标 gpt-6-luna / xhigh；本工具未暴露可独立核实的实际 provider/model/effort，按主控指令如实记为不可核实  
contract_version: 1.0.0

## completed

- Added runtime-only `TingLu.Levels.StreetLevel` and `StreetLevelFactory.Create()` in `Assets/TingLu/Levels/StreetLevel.cs`. It exposes immutable copies of the fixture lists and stays separate from the legacy `LevelDocument` JSON/editor format.
- Added `Assets/TingLu/Scenes/Levels/StreetCrossroads.unity`, a minimal scene containing the `StreetEntry` component reference. It references the stable script GUID `88a335567c78436e97a3b26d0b733e90`.
- Added `Assets/TingLu/Tests/Upgrade/Role09/StreetLevelFactoryTests.cs` for catalog/stable ID checks, checkpoints and goal, detours and route reachability using real `StreetWorld`, signal stop-line/phase behavior, and two simulated signal cycles of continuing one-way traffic. It also checks building clearance and ordinary static collision for the parked car. The test asmdef now includes `TingLu.World`; this shared assembly-definition edit was made by 00, not Role09.
- Added Unity `.meta` files for the new runtime class, scene, and test source.
- No public contract, editor, bootstrap, Build Settings, input, or shared config changes were made by Role09. The scene cannot become the active entry until 00 enables the integration define and handles Build Settings.

## layout and fixture IDs

Coordinates use `Point2(X,Y)` with north at `+Y`, mapped to Unity `(X,height,Y)`. Numeric values are local integration baselines pending playtesting.

- Stable level: `street.crossroads.v1`; title `十字路口`; spawn `(-5.5,-16)` facing north.
- Main road strips have half-width 4m and extend to ±24m. Sidewalk strips extend from the road edges to 7m. Four crosswalk surfaces occupy the four approaches, with ramps, curbs, tactile guide paths, and `tactile.dots` corner/approach patches.
- Checkpoints are `checkpoint.west.approach` at `(-5.5,5.3)` and `checkpoint.east.landing` at `(5.5,5.3)`. Goal `(9.45,18)`, radius `0.85m`, lies in the northeast alley bounded by x=8.2..10.7 (2.5m clear width).
- Permanent buildings: `building.nw`, `building.sw`, `building.se`, `building.ne.west`, and `building.ne.east`. The split northeast pair leaves the alley open.
- Temporary obstructions: `obstacle.bicycle.west` at `(-5.5,-10)` (tactile path; solid, probe-blocking, ordinary non-vehicle collision, contact damage 15); `obstacle.cardboard.east` at `(5.5,9.3)`; `obstacle.barrier.se.edge` at `(5.9,-13)`; parked vehicle `vehicle.parked.se` at `(3.1,-14)` (solid, ordinary non-vehicle collision, contact damage 15).
- Active signals: `street.signal.ns` and `street.signal.ew`. Four one-way car routes/objects are `vehicle.ns.southbound`, `vehicle.ns.northbound`, `vehicle.ew.eastbound`, and `vehicle.ew.westbound`; the fixture also has routed `rider.east.sidewalk`, routed `walker.north.west`, and stationary `animal.bird.ne`.
- Permanent buildings are solid and probe-blocking; each has initial acoustic attenuation `0.7` (a shared-world convention and tuning value, not a measured material response).
- All fixture surface/object material IDs and sound IDs come from `StreetCatalog`; no new global directory IDs were introduced. No bus stops, shops, tree pits, poles, or manholes are placed.

## traffic and geometry review

- Crosswalk outer edges are ±5.9m. Each vehicle route is a directed loop at speed 5.5m/s with one straight main-road pass per lap. Stop line distance and starting offset are both 19.8m from each route's `Points[0]` at ±27m, so initial centers are at x/y=±7.2m. The 0.8m vehicle half-length leaves 0.5m clearance to the crosswalk edge. Each route reaches its stop line only once per lap, so the existing single `StopLineDistance` controls every intersection approach for that vehicle.
- The one-way return legs avoid reversing through the intersection and stay at least 0.85m outside all permanent building polygons: southbound exits via x=-32 then y=32; northbound via x=32 then y=-32; eastbound via x=27/y=36/x=-36; westbound via y=-36/x=36. These four return paths are staggered so they do not share the same outer lane. The Role09 test checks segment clearance with an expanded 0.85m building footprint and simulates 68.2s at 20ms steps, crossing the configured 34s signal cycle twice.
- The fixture uses the configured 14s `ns` green, 3s clearance, 14s `ew` green, 3s clearance cycle. Signal object IDs match their sound IDs; `StreetWorld` derives active sound state from phase.
- Static path review followed the test's 0.02s steps at the configured 1.2m/s walk speed. The player reaches the west checkpoint after about 18.1s; the NS routes leave the approach but continue circulating and return for following signal cycles, so the traffic is not a finite startup wave. The east detour passes below the cardboard, enters the 2.5m northeast alley, and reaches the goal without intersecting permanent building polygons. This is source/geometry reasoning, not a test result.
- The public display contract has no traffic-light visual-state API. The fixture represents signal timing and audio state only; it intentionally adds no misleading static light prop.

## changed_files

- `Assets/TingLu/Levels/StreetLevel.cs` and `.meta`
- `Assets/TingLu/Scenes/Levels/StreetCrossroads.unity` and `.meta`
- `Assets/TingLu/Tests/Upgrade/Role09/StreetLevelFactoryTests.cs` and `.meta`
- `docs/handoffs/UPGRADE_09.md`
- 00 separately added `TingLu.World` to `Assets/TingLu/Tests/Upgrade/Role09/TingLu.Tests.Upgrade.Role09.asmdef` references.

## integration

- The scene root binds the script GUID for `Assets/TingLu/Bootstrap/Runtime/StreetEntry.cs`. `StreetEntry.cs` remains wrapped by `TINGLU_STREET_INTEGRATED`; the scene is not yet a playable integration on its own.
- The 00-owned `StreetSession` draft calls `StreetLevelFactory.Create()` and consumes `Id`, `Title`, `GoalText`, `Spawn`, `Surfaces`, `Objects`, `Routes`, `Checkpoints`, `Goal`, and `GoalRadius`.
- 00 owns removing/setting the integration define, selecting the scene in Build Settings, and running the exclusive Unity import/test/build window. Role09 did not open Unity or modify these files/settings.

## tests

- NOT_RUN: Unity compilation/import, Role09 EditMode tests, PlayMode, integrated scene launch, build, full-route A30, audio playback, or visual rendering. Unity is owned/exclusively coordinated by 00; no Unity process was started by Role09.
- Static review only: factory constructor signatures and IDs were compared with `StreetContracts.cs`, `StreetCatalog`, `StreetWorld`, and `StreetConfig`; route distances and the fixed test walk path were checked against the map geometry. This is not a compile or test PASS.
- The route test is a deterministic fixed-step logical reachability check, not human playtesting or proof of an entire A30 run.

## limitations and next dependencies

- This minimal scene only binds `StreetEntry`; all map geometry is runtime data and visible presentation/audio depend on the other roles and 00 integration.
- All coordinates, speeds, stop margin, and surface layout are initial tuning values. Tactile-cue continuity, vehicle audio, signal representation, visual rendering, and actual player feel still require integrated/human review.
- First action for 00: run the new Role09 EditMode tests during the exclusive Unity window, fix any compiler/test failures within assigned ownership, then integrate the scene. Preserve `NOT_RUN` above until actual evidence is available.
- Role12 must independently decide the acceptance items and full A30; this handoff does not claim any A-item PASS.

## contract_requests

- None. No public contract changes requested.
