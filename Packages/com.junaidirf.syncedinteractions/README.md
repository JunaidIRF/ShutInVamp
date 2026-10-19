# Synced Interactions for Unity

A modular, zero-controller-edit system for synchronized multi-character animations: executions, paired combat, handshakes, co-op interactions, and group cinematics.

Plays animation clips directly on character `Animator`s via Unity Playables without altering your existing Animator Controllers or state machines. Characters align onto slots, lockstep from a unified clock with zero drift, fire modular gameplay events, and blend seamlessly back to their regular gameplay states.

---

## Key Highlights

- **Zero Controller Pollution**: Driven entirely by `AnimationPlayableOutput`. Your controllers, transitions, and layers are never modified.
- **Unified Dopesheet Authoring ("Animate Both")**: Edit keyframes for both characters on a single dopesheet in Unity's standard **Animation Window** with zero missing bone warnings. Automatically splits and saves back to individual clips.
- **Interactive Timeline Stage ("Open on Stage")**: Preview synchronized playback with a scratch Timeline, reposition character slots with Scene View gizmos, and retime draggable event markers.
- **Fast Scene Bone Targeting**: Select any bone in your hierarchy or scene and click **`Use Selected: "<TransformName>"`**, drag-and-drop directly into bone fields, or browse the skeleton tree.
- **Modular Game Actions**: Add game-specific logic (dismemberment, camera shake, blood FX, audio) by subclassing `InteractionAction`, with live timeline scrubbing previews via `IInteractionPreviewAction`.
- **Dynamic Vetoes & Lifecycle**: Prevent invalid interactions with `IInteractionVeto` and freeze/unfreeze character AI or input with `IInteractionParticipant`.

---

## Installation

### Via Package Manager (Git URL)
In the Unity Editor:
**Window &rarr; Package Manager &rarr; [+] Add package from git URL...**
```
https://github.com/JunaidIRF/SyncedInteractions.git
```

Or add directly to `Packages/manifest.json`:
```json
{
  "dependencies": {
    "com.junaidirf.syncedinteractions": "https://github.com/JunaidIRF/SyncedInteractions.git"
  }
}
```

---

## Quick Start (3 Steps)

### 1. Add `InteractionActor`
Attach `InteractionActor` to each participating character with an `Animator`:
```
Character
├── InteractionActor (Component)
└── Animator
```
> [!NOTE]
> If the character's `Animator` is on a child GameObject (e.g. FPS hands or sub-model), drag that child into the actor's **Animator** property.

### 2. Create the Interaction Asset
Right-click in the Project Window:
**Create &rarr; Synced Interactions &rarr; Interaction**
- Define roles (e.g. `Attacker` and `Victim`) with their animation clips and preview prefabs.
- Set relative slot positions, alignment mode (`Glide` / `Warp`), and ground snapping.
- Click **"Animate Both (Animation Window)"** to keyframe both characters together.
- Click **"Open on Stage (Timeline)"** to scrub and retime events in real time.

### 3. Play via Code or Trigger
```csharp
using UnityEngine;
using JunaidIRF.SyncedInteractions;

public class ExecutionExample : MonoBehaviour
{
    [SerializeField] InteractionAsset executionAsset;
    [SerializeField] InteractionActor playerActor;

    public void TriggerExecution(InteractionActor enemyActor)
    {
        Interaction.Play(executionAsset, playerActor, enemyActor);
    }
}
```

---

## Authoring Modes Comparison

| Feature | Animate Both (Dual Rig) | Open on Stage (Timeline) |
| :--- | :--- | :--- |
| **Window Used** | Unity Animation Window | Unity Timeline Window |
| **Primary Purpose** | Keyframing, tweaking curves, posing | Scrubbing, slot layout, retiming events |
| **Dopesheet View** | Both characters on **one** dopesheet | Separate tracks per character |
| **Keyframe Editing** | Full curve and dopesheet tools | Right-click clip &rarr; edit in Animation |
| **Slot Positioning** | Fixed at slot offsets | Interactive move gizmo in Scene View |
| **Event Markers** | Animation event markers on dopesheet | Draggable markers on Timeline track |
| **Write-Back** | Automatic curve splitting & saving | Real-time asset synchronization |

---

## Documentation

For the complete architectural manual, custom action code templates, and API reference, see:
- **[Full User Manual & Architecture Guide](Documentation~/Manual.md)**
- **[Architecture Overview](Documentation~/index.md)**

---

## License
MIT License. See [LICENSE.md](LICENSE.md).
