# Synced Interactions Documentation

A modular, zero-controller-edit system for synchronized multi-character animations, executions, and paired actions in Unity.

- **[Full User Manual & Integration Guide](Manual.md)**: Detailed step-by-step instructions for setup, dual-rig dopesheet editing, Timeline scrubbing, bone picking, and custom actions.

---

## Architecture Overview

### 1. InteractionAsset
A ScriptableObject defining an interaction:
- **Roles:** List of `InteractionRole`s. Each role specifies its name, `AnimationClip`, preview prefab, relative slot offset, yaw, and blend times.
- **Anchor:** The frame of reference. `Role` uses one participant's world position as the origin; `World` uses a designated world pose.
- **Alignment:** How characters reach their slots before playing (`Glide`, `Warp`, or `None`).
- **Events:** Timed markers containing modular actions executed at specific points during playback.

### 2. Dual Rig & Timeline Authoring
- **Animate Both (Animation Window)**: Creates a single composite rig with both characters' curves combined on one unified dopesheet. Keyframe, scrub, and pose both characters together with zero missing bone warnings. Automatically writes back to individual clips.
- **Open on Stage (Timeline)**: Previews both characters in sync on a scratch Timeline. Move characters in Scene view to adjust slot positions; drag event markers on the timeline track to retime events.

### 3. InteractionActor
Attached to any GameObject representing an interactable character with an `Animator`.
- Discovers movement adapters (`IInteractionMover`) automatically (Transform, CharacterController, NavMeshAgent, Rigidbody).
- Dispatches start and end notifications to `IInteractionParticipant` components.
- Tracks active `InteractionHandle` and busy state.

### 4. Custom Actions & Event Previews
- **`InteractionAction`**: Subclass this in your project to create custom game mechanics (damage, dismemberment, camera shake, blood FX). Automatically appears in the inspector's "Add Action" menu.
- **`IInteractionPreviewAction`**: Implement on custom actions to preview their visual effects (e.g. severed limbs, spawned props) on stage when scrubbing past their event time.

### 5. Execution & Sync
`Interaction.Play(asset, cast)` instantiates an `InteractionHandle` and registers it with the internal `InteractionRunner`.
- Each participant gets an `AnimationClipPlayable` connected to an `AnimationPlayableOutput`.
- All graphs are advanced on the same frame from a unified master clock, preventing drift.
- When finished or cancelled, all playables are destroyed and the actor's original animator state and mover settings are restored.

### 6. Discovery & Veto System
- **`InteractionQuery`**: Evaluates eligible interaction partners using distance, FOV cone, line of sight, and `InteractionCondition` filters.
- **`InteractionTrigger`**: Detects targets in real-time, displays interaction prompts, and triggers interactions on key press.
- **`IInteractionVeto`**: Allows gameplay scripts to dynamically forbid interactions when appropriate.
