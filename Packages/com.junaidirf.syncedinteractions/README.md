# Synced Interactions for Unity

A modular Unity package for synchronized multi-character animations: paired handshakes, hugs, executions, takedowns, co-op doors, and group actions.

Plays animation clips directly on character `Animator`s via Playables with zero controller edits. All characters align onto authoring slots, step in lockstep from a single synchronized clock, fire timed gameplay actions, and blend cleanly back to their regular animator states.

---

## Features

- **No Controller Edits:** Plays clips directly over existing controllers via `AnimationPlayableOutput` and blends out weight smoothly.
- **Any Rig Type:** Retargets Humanoid clips to any humanoid character; supports Generic rigs via matching bone hierarchies.
- **Multi-Role:** Supports 2-character interactions (Initiator & Receiver) or N-character group interactions.
- **Placement & Alignment:** Characters glide or warp into relative slots with optional ground raycast snapping.
- **Timeline Stage Authoring:** Click "Open on Stage" on any `InteractionAsset` to scrub in sync on a scratch Timeline, drag event markers, key poses in Unity's Animation Window, and preview in real time with auto write-back.
- **Discovery & Querying:** `InteractionQuery` and `InteractionTrigger` find eligible partners in range and FOV, checking line-of-sight and customizable `[SerializeReference]` conditions.
- **Veto Hooks:** Characters can veto interactions at runtime via `IInteractionVeto` (e.g. stunned state, dead, busy).
- **Clean Fallback & Cancel:** Participant destruction, cancellation, or interruption restores controller states, movers, and rendering layers without leaving characters frozen.

---

## Installation

### Via Git URL
Add the package to your `Packages/manifest.json`:
```json
"com.junaidirf.syncedinteractions": "https://github.com/JunaidIRF/SyncedInteractions.git"
```
Or use the Unity Package Manager: **Add package from git URL...** > `https://github.com/JunaidIRF/SyncedInteractions.git`

---

## Quick Start

### 1. Set Up Characters
Add an `InteractionActor` component to each character with an `Animator`.

### 2. Create an Interaction Asset
Right-click in the Project view > **Create > Synced Interactions > Interaction**.
- Add roles (e.g. `Initiator` and `Receiver`).
- Assign animation clips and preview prefabs to each role.
- Configure alignment (Glide or Warp) and slot offset/facing.
- Add timed events with built-in or custom actions (sound, spawn prefab, invoke event, etc.).
- Click **Open on Stage** to preview and adjust timings in Timeline.

### 3. Play by Code or Trigger
```csharp
using JunaidIRF.SyncedInteractions;

// Play directly by code
Interaction.Play(handshakeAsset, playerActor, npcActor);
```
Or attach an `InteractionTrigger` to the player with key `[E]` and prompt UI to automatically detect and trigger nearby interactions.

---

## License
MIT License. See [LICENSE.md](LICENSE.md).
