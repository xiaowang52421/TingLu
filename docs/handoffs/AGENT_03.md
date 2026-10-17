status: IMPLEMENTED_SOURCE_NOT_UNITY_VERIFIED
contract_version: 0.2.0

completed:
- `AcousticModel` samples active sound sources once, computes the shared final discernibility/gain from distance and occlusion, and carries known semantic/category/material metadata from the definition for 02. The sample does not query again during playback.
- `UnityAudioPlayback` uses `AudioRequest.Gain` directly and a flat custom Unity rolloff curve; Unity does not apply a second distance rolloff. `UpdateSource(eventIdOrSourceId, position, gain, occlusion)` updates a running source from the same shared sample. Occlusion is recorded alongside the supplied final gain and is not multiplied a second time.
- Each pooled AudioSource lives on its own child GameObject, so moving one sound does not move other voices or the backend owner. Dispose destroys those child objects.
- Listener ownership stays with Runtime. Assign the scene listener with `ConfigureListener(Transform)` and then call `Initialize(IGameClock, recoveryGeneration)`. Inject that same component as `IAudioPlayback` and `IAudioSpeech`.
- Speech requests pass through a stable priority queue; a newer request replaces pending speech with the same supersession key. While paused, interface speech is still eligible even when higher-priority world speech waits. Paused world audio remains pending until resume, when playback is issued and `Started` is reported. Speech WAV rendering runs on a bounded worker and Unity clip/source calls and receipts stay on the Unity main thread. Reset generations discard old output. Active voice speech ducks ambience.
- Windows voice rendering prefers a Chinese `System.Speech` voice, then uses the built-in SAPI COM voice/file stream path when the assembly is unavailable. Both paths fail clearly if no Chinese voice is installed; SAPI COM objects and temporary WAV files are released.
- Stop, reset, preemption, natural completion, and dispose release pooled clips/sources; dispose invalidates late worker results. Windows `System.Speech` remains a late-bound optional provider and reports unavailable/render failures in the shared receipt event.

changed_files:
- `Assets/TingLu/Audio/AcousticModel.cs`
- `Assets/TingLu/Audio/UnityAudioPlayback.cs`
- `Assets/TingLu/Audio/SpeechPriorityQueue.cs` (+ `.meta`)
- `Assets/TingLu/Audio/UnityAudioPlayback.cs.meta`
- `Tests/Accessibility/AudioChecks.cs`
- `docs/handoffs/AGENT_03.md`

integration:
- Runtime creates one `AudioListener`, calls `ConfigureListener(listenerTransform)` then `Initialize(clock, generation)`, subscribes to `Receipt`, and disposes when the owner tears down.
- Route each sampled source to `new AudioRequest(eventId, soundId, sourcePosition, priority, generation, sourceId, sampled.Discernibility, sampled.Occlusion, loop)`. The gain is already the model's final gain. For a running loop, call `UpdateSource(sourceId, position, sampled.Discernibility, sampled.Occlusion)`; no public interface change is required for that concrete backend helper.
- Build acoustic profiles once and share the same `AcousticModel.Sample` result between presentation/perception and playback. The semantic/category/material values now come from the permitted `WorldObjectState.Definition` data and are copied into `AcousticQueryResult`.
- Runtime owns the listener transform, simulation ticking, pause/restore ordering, generation increments, and scene lifetime.

verification:
- PowerShell `Add-Type` compiled all Contracts plus `AcousticModel`, `SpeechPriorityQueue`, `AccessibleMenu`, and the two pure check classes. Acoustic gain/metadata, queue priority/supersession, menu, pause/retry/results, and settings checks passed.
- `Verify-AudioUi.ps1` compiled and invoked the extracted production speech helper without Unity. It generated a valid 22,050 Hz mono PCM WAV with 70,306 frames for Chinese test text (`WAV_SYNTHESIS_ONLY`). This verifies synthesis and WAV parsing only; it does not verify Unity playback or audio hardware.
- Unity editor/import/playback, audio hardware, four-bearing listening, hardware delay, and device interruption were not run. HRTF/Wwise and mobile remain unverified.
- Generated tones are temporary placeholder cues, not authored material sounds. This source handoff does not claim audio recognizability or a playable pure-audio level.

contract_requests: none for 0.2.0.
