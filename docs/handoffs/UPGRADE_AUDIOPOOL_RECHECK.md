# Audio voice-pool recheck handoff

## Finding

`output/unity-verification/20261015-190344-f0c2524b/unity.log` contains 453 `voice_limit_no_lower_priority_voice` warnings between lines 431 and 9769. The playback backend emits `Failed` from `Play` synchronously when its pool has no strictly lower-priority victim. `StreetAudioController` had already inserted the request into its active maps, so the receipt removed it immediately; the next `StreetSession.RefreshSenses` call tried the same source again. `StreetSession.Tick` calls `RefreshSenses` inside each fixed simulation substep (maximum step 0.02 seconds), which explains the repeated warning stream. This was a retry on subsequent syncs, not an inner retry loop in one `Sync` call.

`UnityAudioPlayback.AcquireVoice` only evicts a voice whose priority is strictly below the new request. Equal-priority voices are never displaced. Their admission order previously followed acoustic result enumeration, so reordering the world snapshot could change which candidates occupied a constrained pool. Vehicle hazards already request `CriticalThreat`; that priority remains intact.

The production backend's `Stop` emits `Stopped` synchronously. Before this fix, the controller's receipt handler removed the active/event mappings during `StopVoice`, followed by a second removal after `Stop` returned. That path did not invalidate a live dictionary enumeration: `StopAll` snapshots event keys first, and `StopVoice` does not enumerate those maps. Detaching the mappings before calling `Stop` now makes the reentrant receipt a no-op and keeps cleanup ownership in the controller.

## Change

- A failed source retries by simulation-time exponential backoff: 0.5 seconds, 1 second, then 2 seconds, with a 2 second maximum delay thereafter. The retry state is keyed by source, sound, and recovery generation, so a pool slot that later opens can eventually restore a still-valid source. `Started` clears the backoff; leaving the desired acoustic sample, changing the sound, or advancing the recovery generation also resets it. Paused syncs at an unchanged `SampledAt` do not advance retries.
- Sync sorts traffic hazards before ambience and then sorts by ordinal source ID. Same-priority pool admission is stable when snapshot enumeration changes; critical traffic keeps its existing priority.
- `StopVoice` and `StopAll` detach receipt mappings before backend stops, so synchronous stop receipts cannot mutate the state being cleaned up.
- The role test fake can emit repeated pool failures and synchronous start/stop receipts. Added cases cover 0.5/1/2 second backoff under 0.02 second substeps, successful recovery after two rejected plays, unchanged paused sample time, reset/removal recovery, vehicle-first priority, stable same-priority ordering, sound replacement, and synchronous stop reentry.

## Scope and validation

Changed files: `Assets/TingLu/Audio/StreetAudioController.cs` and `Assets/TingLu/Tests/Upgrade/Role06/StreetAcousticModelTests.cs`. No voice-limit increase or `UnityAudioPlayback.cs` change was made. The change is source-frozen for main-controller validation.

Unity was not launched for this handoff. The main controller owns exclusive validation; run the relevant role test and required integrated Unity check there, then append results here. Device audio and human listening remain outside this source-level recheck.
