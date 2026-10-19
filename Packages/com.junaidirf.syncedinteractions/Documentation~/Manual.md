# Synced Interactions User Manual & Integration Guide

A complete, practical guide for authoring, customizing, and triggering synchronized multi-character animations in Unity.

---

## Table of Contents
1. [Core Principles](#1-core-principles)
2. [Character Setup](#2-character-setup)
3. [Creating an Interaction Asset](#3-creating-an-interaction-asset)
4. [Authoring Workflow 1: Dual Rig (Animation Window)](#4-authoring-workflow-1-dual-rig-animation-window)
5. [Authoring Workflow 2: Timeline Stage](#5-authoring-workflow-2-timeline-stage)
6. [Bone Targeting & Scene Selection](#6-bone-targeting--scene-selection)
7. [Custom Game Actions (Dismemberment, VFX, Audio)](#7-custom-game-actions)
8. [Runtime Triggering & Gameplay Integration](#8-runtime-triggering--gameplay-integration)
9. [Vetoes & Participant Callbacks](#9-vetoes--participant-callbacks)

---

## 1. Core Principles

- **No Controller Pollution**: Plays clips directly on `Animator` components using Unity's `Playables` API (`AnimationPlayableOutput`). Your existing Animator Controllers, transitions, and layers are never touched or modified.
- **Master Clock Sync**: Advances all participant playables from a single shared delta clock each frame, guaranteeing zero drift regardless of framerate.
- **Strict Modularity**: The package contains zero game-specific logic. Gameplay mechanics (dismemberment, blood decals, screen shakes, health subtraction) are authored as self-contained `InteractionAction` classes in your game code.

---

## 2. Character Setup

Every character participating in an interaction needs an `InteractionActor` component:

```
Character GameObject
├── InteractionActor (Component)
│   └── Animator: [Assigned Animator] (Leave empty to auto-find in children)
└── Animator (Humanoid or Generic)
```

1. Select your character prefab (e.g., `Player.prefab`, `Enemy.prefab`).
2. Add component: **`Synced Interactions > Interaction Actor`**.
3. **Animator Assignment**:
   - If the `Animator` is on the root GameObject, leave the **Animator** field empty (it auto-detects it).
   - If the `Animator` is on a child GameObject (e.g. `Model` or `Camera Head`), drag that child into the **Animator** field.
4. **Movement Adapter** (`IInteractionMover`):
   - Automatically detects whatever character controller is present: `CharacterController`, `NavMeshAgent`, `Rigidbody`, or raw `Transform`. No extra configuration needed.

---

## 3. Creating an Interaction Asset

Right-click in Project view > **Create > Synced Interactions > Interaction**.

### Key Fields:
- **Roles**:
  - `name`: Identifier for the role (e.g., `Attacker`, `Victim`, `Leader`, `Follower`).
  - `clip`: The `AnimationClip` for this character.
  - `previewPrefab`: The prefab instantiated when authoring on stage or dual rig.
  - `offset`: Relative position $(X, Y, Z)$ from the anchor role.
  - `yaw`: Facing rotation in degrees relative to the anchor.
  - `blendIn` / `blendOut`: Crossfade duration in seconds.
  - `lookAtRole` / `lookAtBone`: (Optional) Automatically pitch/aim towards another role or specific bone.
- **Anchor**:
  - `Role`: Anchor to a role (e.g. `Victim` stays in place, `Player` moves to slot).
  - `World`: Anchor to interaction trigger position.
- **Alignment**:
  - `Glide`: Smoothly lerps characters into slot positions over `alignTime` before starting.
  - `Warp`: Instantly teleports characters to slots.
  - `None`: Plays animations in current world poses.
- **Snap To Ground**: Raycasts down onto `groundMask` to position characters on uneven terrain.

---

## 4. Authoring Workflow 1: Dual Rig (Animation Window)

Use this when you want to **animate or tweak keyframes of both characters simultaneously** on a single unified dopesheet (similar to custom multi-character authoring rigs).

### How to use:
1. Select the `InteractionAsset` in the Project view.
2. Click **"Animate Both (Animation Window)"** in the Inspector.
3. Unity opens a temporary authoring scene:
   - Creates root `[Interaction Rig: <Asset>]` with an Animator and editing controller.
   - Parents both characters under the rig as `Role0` and `Role1` at their authoring slots.
   - Automatically strips nested child animators so only the root rig drives the hierarchy.
   - Combines all curves into one editing clip: `<Role0>/<BonePath>` and `<Role1>/<BonePath>`.
   - Opens Unity's **Animation Window** with all keyframes visible together.
4. **Scrub & Edit**:
   - Both characters are visible on the dopesheet and curve graph.
   - Move bones, rotate joints, adjust timing, or record keyframes using Unity's standard Animation tools.
   - Drag event markers on the animation timeline to retime events.
5. **Save & Exit**:
   - Edits automatically write back to each individual `role.clip` as you work.
   - Click **"Apply Now"** in the Inspector to force an immediate save.
   - Click **"Apply & Close Dual Rig"** to commit all changes, restore your previous scene, and delete the temporary staging folder.

---

## 5. Authoring Workflow 2: Timeline Stage

Use this when you want to **preview the interaction in sync, adjust slot placement interactively, and retime events**.

### How to use:
1. Select the `InteractionAsset`.
2. Click **"Open on Stage (Timeline)"**.
3. Unity creates a scratch scene with a Timeline:
   - One Animation Track per role, bound to each preview character.
   - A Marker Track showing all `InteractionEvent`s as draggable markers.
4. **Interact**:
   - Scrub the Timeline playback head to inspect the animation in real time.
   - Drag a character in the Scene view with Unity's Move tool: its slot offset updates in the asset automatically.
   - Drag an event marker on the marker track: the event's `time` updates automatically.
   - Right-click the marker track to add a new event; delete a marker to remove an event.
5. Click **"Close Timeline Stage"** to return to your scene.

---

## 6. Bone Targeting & Scene Selection

When adding events that attach effects, sounds, or damage to specific bones (e.g. bite neck, slash chest, grab hand):

1. Under **Events**, find your event.
2. In the **Role** field, choose which character owns the target bone.
3. In the **Bone** dropdown:
   - **`Use Selected: "<TransformName>"`**: Click any GameObject or bone in the Scene view / Hierarchy, then click this option to instantly assign it.
   - **Drag-and-Drop**: Drag any bone or GameObject directly from the Hierarchy or Project into the Bone field.
   - **Preview bones >**: Browse the full bone hierarchy tree of the role's preview prefab.
   - **Humanoid >**: Pick standard humanoid body parts (`Head`, `LeftHand`, `Chest`, etc.).
   - **(root)**: Targets the character's base Transform.

---

## 7. Custom Game Actions

Actions are modular `[SerializeReference]` classes that execute when an event fires. Anyone can write custom actions for their game without editing package code.

### Step 1: Create a Custom Action Class

Create a new C# script anywhere in your project's `Assets` folder (e.g. `Assets/Scripts/Interactions/DismemberAction.cs`):

```csharp
using System;
using UnityEngine;
using JunaidIRF.SyncedInteractions;

[Serializable]
public class DismemberAction : InteractionAction, IInteractionPreviewAction
{
    [Tooltip("Which body part or limb to sever")]
    public string bodyPart = "LeftArm";

    [Tooltip("Prefab spawned when limb is detached")]
    public GameObject severedLimbPrefab;

    /// <summary>Called when the event fires at runtime during gameplay.</summary>
    public override void Fire(InteractionContext context)
    {
        // context.actor: The character whose role is assigned to this event
        // context.bone:  The resolved target bone transform
        // context.handle: The running interaction handle
        // context["OtherRole"]: Access another participant actor by role name

        if (context.bone == null) return;

        Debug.Log($"Severing {bodyPart} on {context.actor.name} at bone {context.bone.name}");

        // Example: Disable bone renderer / hide mesh
        var renderer = context.bone.GetComponentInChildren<Renderer>();
        if (renderer != null) renderer.enabled = false;

        // Example: Spawn severed limb prop
        if (severedLimbPrefab != null)
        {
            GameObject.Instantiate(severedLimbPrefab, context.bone.position, context.bone.rotation);
        }
    }

    /// <summary>Optional: Called when interaction ends (clean up temporary state).</summary>
    public override void End(InteractionContext context, bool cancelled)
    {
        // Clean up or finalize state if necessary
    }

    // ─────────────────────────────────────────────────────────────
    // Stage Preview Support (IInteractionPreviewAction)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Called while scrubbing on stage when the playhead reaches or leaves this event.</summary>
    public void Preview(GameObject character, Transform bone, bool reached)
    {
        if (bone == null) return;
        var renderer = bone.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            renderer.enabled = !reached; // Hide limb when scrubbed past the event
        }
    }

    /// <summary>Called when authoring stage closes to restore original prefab state.</summary>
    public void EndPreview(GameObject character)
    {
        foreach (var r in character.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = true;
        }
    }
}
```

### Step 2: Use It in the Inspector
Because `DismemberAction` inherits from `InteractionAction`, Unity automatically includes it in the **"Add Action"** dropdown on any event in your `InteractionAsset`!

---

## 8. Runtime Triggering & Gameplay Integration

### Trigger by Code:
```csharp
using UnityEngine;
using JunaidIRF.SyncedInteractions;

public class ExecutionController : MonoBehaviour
{
    [SerializeField] InteractionAsset biteInteraction;
    [SerializeField] InteractionActor playerActor;

    public void ExecuteEnemy(InteractionActor enemyActor)
    {
        // Plays interaction with player as Role 0 and enemy as Role 1 (or role name mapping)
        InteractionHandle handle = Interaction.Play(biteInteraction, playerActor, enemyActor);

        // Optional callbacks
        handle.OnFinished += (h) => Debug.Log("Execution completed successfully!");
        handle.OnCancelled += (h) => Debug.Log("Execution was interrupted!");
    }
}
```

### Trigger by Proximity & Prompt (`InteractionTrigger`):
1. Attach `InteractionTrigger` to an interactable object or NPC.
2. Assign the `asset`.
3. Set `activationMode = PromptAndKey`, and assign a key (e.g. `KeyCode.E`).
4. When a player with an `InteractionActor` enters the trigger radius, the trigger detects the player and fires `onPromptStateChanged(true)`. Pressing `E` starts the interaction.

---

## 9. Vetoes & Participant Callbacks

### Dynamic Vetoing (`IInteractionVeto`):
Implement `IInteractionVeto` on any component on the character (or its Animator) to prevent interactions under specific conditions (e.g. enemy is not stunned, character is already dead, invulnerable):

```csharp
using UnityEngine;
using JunaidIRF.SyncedInteractions;

public class EnemyHealth : MonoBehaviour, IInteractionVeto
{
    public bool isStunned;
    public bool isDead;

    public bool CanInteract(InteractionActor partner, InteractionAsset asset, string role, out string reason)
    {
        if (isDead)
        {
            reason = "Enemy is dead";
            return false;
        }
        if (!isStunned && role == "Victim")
        {
            reason = "Enemy is not stunned";
            return false;
        }
        reason = null;
        return true;
    }
}
```

### Participant Lifecycle (`IInteractionParticipant`):
Implement `IInteractionParticipant` on your movement or AI scripts to cleanly freeze AI/input during interactions:

```csharp
using UnityEngine;
using JunaidIRF.SyncedInteractions;

public class EnemyAI : MonoBehaviour, IInteractionParticipant
{
    public void OnInteractionStart(InteractionHandle handle, string role)
    {
        // Disable AI navigation and attack logic while in interaction
        enabled = false;
    }

    public void OnInteractionEnd(InteractionHandle handle, string role, bool cancelled)
    {
        // Re-enable AI when interaction finishes or is cancelled
        enabled = true;
    }
}
```
