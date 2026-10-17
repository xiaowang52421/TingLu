# D12 Ramp and Save Acceptance Handoff

Date: 2026-10-17  
Scope: ramp direction/height acceptance and elevation persistence only. Unity execution remains with the main controller.

## Geometry finding

The original ramp setup selected its slope axis by comparing the absolute X/Y coordinates of each polygon center. That comparison is unrelated to the curb being crossed. The actual north/south crosswalks span X and connect the west/east sidewalks across the north/south road, so all four `ramp.ns.*` surfaces must slope along X. The west/east crosswalks span Y and connect the south/north sidewalks across the east/west road, so all four `ramp.ew.*` surfaces must slope along Y. The previous branch selected the other axis for all eight ramps.

The main controller corrected `StreetLevel.AddSurface` to select axis from the ramp family and derive the plane from its 0.7 m curb-crossing span. The road-side ends are at X/Y ±3.8 and height 0; the sidewalk-side ends are at X/Y ±4.5 and height 0.15. The middle of each ramp is 0.075. This models the public crossing profile and leaves ramp side edges outside this check; those edges may meet adjacent curb faces as steps.

## Added acceptance coverage

- `StreetLevelFactoryTests.EightRampsConnectRoadAndSidewalkAtTheCorrectPublicEdges` checks all eight named ramps, the correct slope axis, both ends against both the ramp plane and the winning road/sidewalk/crosswalk surfaces, and the midpoint at 0.075.
- `StreetLevelFactoryTests.EveryCrosswalkDirectionIsWalkableWithAContinuousEffectiveHeightProfile` checks static movement and the effective overlaid surface height along all four complete crossing directions. It samples every 2 cm from sidewalk to sidewalk; the maximum adjacent change is bounded by 0.0045, matching a 0.15 m ramp over 0.7 m.
- `StreetSaveServiceTests.PerceptionMemorySaveLoadAndRestorePreserveRampHeightPlane` passes a real ramp through `StreetPerception.Probe`, `StreetMemory.Apply/Export`, save, load, and `StreetMemory.Import(stale: true)`. It checks the observed ramp height at a known point, all plane coefficients, restored height, and stale timing.
- `StreetSaveServiceTests.SchemaOnePayloadWithoutOptionalHeightFieldsDefaultsToFlatGround` removes the optional texture-axis and height fields, recomputes the original envelope checksum, and confirms schema 1 / contract 1.0.0 loads with world-X texture orientation and zero plane coefficients.
- The existing save/load round trip now also asserts that `HeightBase`, `HeightSlopeX`, and `HeightSlopeY` are preserved while loaded memory becomes stale.

The Role10 test assembly gained explicit references to Levels, Memory, Perception, and World so the end-to-end test can exercise the actual pipeline. The main controller approved that test dependency change.

## Verification status

No Unity editor, test runner, or build was started by this task. The new tests and assembly-definition references need the main controller's exclusive EditMode run. They are acceptance assertions, not a passed result. The source implementation correction was made by the main controller in the shared workspace; no other production files were edited here.
