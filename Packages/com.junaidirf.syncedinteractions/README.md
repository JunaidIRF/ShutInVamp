<div align="center">

# 🎭 Synced Interactions for Unity

**Synchronized multi-character animations with zero Animator Controller edits.**<br/>
Paired executions, co-op takedowns, handshakes, hugs, and group interactions driven in lockstep from a single master clock.

[![Unity 6000+](https://img.shields.io/badge/Unity-6000%2B%20%7C%202022.3%2B-blue.svg?style=for-the-badge&logo=unity)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg?style=for-the-badge)](LICENSE.md)
[![Status: Production Ready](https://img.shields.io/badge/Status-Production%20Ready-brightgreen.svg?style=for-the-badge)]()
[![PRs Welcome](https://img.shields.io/badge/PRs-Welcome-purple.svg?style=for-the-badge)]()

[Features](#-features) • [Installation](#-installation) • [Quickstart](#-quickstart-in-3-steps) • [Authoring Modes](#-two-authoring-modes) • [Custom Actions](#-custom-game-actions) • [Runtime API](#-runtime-api)

</div>

---

## ⚡ Highlights

| Feature | What it does |
| :--- | :--- |
| 🛡️ **Zero Controller Pollution** | Plays clips directly over existing controllers via `AnimationPlayableOutput`. State machines are never touched. |
| ⏱️ **Zero-Drift Sync Clock** | Master clock advances all character playables simultaneously, guaranteeing lockstep synchronization at any framerate. |
| 🎬 **Dual-Rig Dopesheet** | Keyframe **both** characters together on a single dopesheet in Unity's standard **Animation Window** with zero missing bones. |
| 📐 **Timeline Staging** | Drag Timeline scrubber to preview sync in real time. Move characters in Scene view to position slots visually. |
| 🎯 **Instant Bone Picker** | Click any bone in the Scene view &rarr; click **`Use Selected`** to bind events instantly. |
| 🧩 **100% Modular Actions** | Subclass `InteractionAction` to add damage, FX, sounds, or dismemberment without touching package code. |

---

## 📦 Installation

### Package Manager (Git URL)
In the Unity Editor:
> **Window** &rarr; **Package Manager** &rarr; **[+]** &rarr; **Add package from git URL...**
```text
https://github.com/JunaidIRF/SyncedInteractions.git
```

### Manual `manifest.json`
Add this line to `Packages/manifest.json`:
```json
"com.junaidirf.syncedinteractions": "https://github.com/JunaidIRF/SyncedInteractions.git"
```

---

## 🚀 Quickstart in 3 Steps

### 1️⃣ Add `InteractionActor` to Characters
Attach `InteractionActor` to your character prefabs.

```
Player / Enemy Prefab
├── InteractionActor (Component)
└── Animator
```
> 💡 *If your `Animator` is on a child object (e.g. FPS arms or inner Model), drag it into the **Animator** field.*

---

### 2️⃣ Create an Interaction Asset
Right-click in Project view:
> **Create** &rarr; **Synced Interactions** &rarr; **Interaction**

1. Add your **Roles** (e.g. `Attacker` and `Victim`).
2. Assign each role's `AnimationClip` and `Preview Prefab`.
3. Set slot offsets $(X, Y, Z)$ and facing `Yaw` angle.
4. Set Alignment (`Glide` or `Warp`) and check `Snap To Ground`.

---

### 3️⃣ Play by Code
```csharp
using UnityEngine;
using JunaidIRF.SyncedInteractions;

public class ExecutionTrigger : MonoBehaviour
{
    [SerializeField] InteractionAsset takedownAsset;
    [SerializeField] InteractionActor playerActor;

    public void Execute(InteractionActor enemyActor)
    {
        // One line triggers the synchronized interaction
        Interaction.Play(takedownAsset, playerActor, enemyActor);
    }
}
```

---

## 🛠️ Two Authoring Modes

<div align="center">

| | 🎬 Animate Both (Animation Window) | ⏱️ Open on Stage (Timeline) |
| :--- | :--- | :--- |
| **Window** | Standard Unity **Animation Window** | Unity **Timeline Window** |
| **Workflow** | Keyframing, tweaking curves, posing | Scrubbing preview, slot placement, event retiming |
| **Dopesheet** | **Both characters on 1 dopesheet** | Separate track per character |
| **Editing** | Add/drag keyframes for any bone on either role | Move characters directly with Scene transform tools |
| **Write-Back** | Auto-splits & saves back to individual clips | Auto-updates asset slot coordinates and event times |

</div>

### 🎬 Mode 1: Animate Both (Animation Window)
Click **"Animate Both (Animation Window)"** on your asset:
- Creates `[Interaction Rig]` with both character models mounted as `Role0` and `Role1`.
- Curves appear on one unified dopesheet: `<Role0>/<Bone>` and `<Role1>/<Bone>`.
- Pose both characters together, scrub attack impact against reaction frames, and hit record.
- Click **"Apply & Close Dual Rig"** to commit curves back to each role's clip.

### ⏱️ Mode 2: Open on Stage (Timeline)
Click **"Open on Stage (Timeline)"** on your asset:
- Playable Timeline with dedicated tracks per character.
- Move or rotate characters in the Scene view: their `offset` and `yaw` save to the asset live.
- Drag markers on the Timeline Marker track to retime events.
- Click **"Close Timeline Stage"** to return.

---

## 🎯 Instant Bone Selection

Selecting bones for impact events, grabs, or visual effects is effortless:

```
┌────────────────────────────────────────────────────────┐
│ Bone: [ mixamorig:LeftShoulder                       ] │
│       ├── 🟢 Use Selected: "mixamorig:LeftShoulder"    │  <-- Click in scene, then click this!
│       ├── 📦 Drag & Drop from Hierarchy / Project     │
│       ├── 🦴 Preview bones > (Browse full skeleton)    │
│       ├── 👤 Humanoid > (Head, Chest, LeftHand...)     │
│       └── ⭕ (root)                                    │
└────────────────────────────────────────────────────────┘
```

---

## 🧩 Custom Game Actions

Actions are modular `[SerializeReference]` classes. Create custom game mechanics (damage, blood splatter, screen shake, dismemberment) without modifying the package:

```csharp
using System;
using UnityEngine;
using JunaidIRF.SyncedInteractions;

[Serializable]
public class BloodSplatterAction : InteractionAction, IInteractionPreviewAction
{
    [Tooltip("VFX Prefab spawned at bone position")]
    public GameObject bloodFxPrefab;
    public float damageAmount = 25f;

    /// <summary>Fired at runtime when the interaction reaches this event.</summary>
    public override void Fire(InteractionContext context)
    {
        // Spawn particle at target bone
        if (bloodFxPrefab != null && context.bone != null)
            GameObject.Instantiate(bloodFxPrefab, context.bone.position, context.bone.rotation);

        // Apply gameplay damage
        if (context.actor != null && context.actor.TryGetComponent<IDamageable>(out var hp))
            hp.TakeDamage(damageAmount);
    }

    /// <summary>Scrub preview on the Timeline authoring stage.</summary>
    public void Preview(GameObject character, Transform bone, bool reached) { }
    public void EndPreview(GameObject character) { }
}
```

> ✨ *Once saved, `BloodSplatterAction` automatically shows up in the Inspector's **"Add Action"** dropdown!*

---

## 🎮 Runtime API

### Dynamic Veto (`IInteractionVeto`)
Implement `IInteractionVeto` on any character script to dynamically block interactions (e.g. target is invulnerable, dead, or not stunned):

```csharp
public class EnemyState : MonoBehaviour, IInteractionVeto
{
    public bool isStunned;

    public bool CanInteract(InteractionActor partner, InteractionAsset asset, string role, out string reason)
    {
        if (role == "Victim" && !isStunned)
        {
            reason = "Enemy is not stunned";
            return false;
        }
        reason = null;
        return true;
    }
}
```

### Freeze AI / Input (`IInteractionParticipant`)
Implement `IInteractionParticipant` on your movement or AI scripts to cleanly pause and resume logic:

```csharp
public class EnemyAI : MonoBehaviour, IInteractionParticipant
{
    public void OnInteractionStart(InteractionHandle handle, string role) => enabled = false;
    public void OnInteractionEnd(InteractionHandle handle, string role, bool cancelled) => enabled = true;
}
```

---

## 📄 License

This package is licensed under the [MIT License](LICENSE.md).
