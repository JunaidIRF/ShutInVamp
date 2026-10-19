# Synced Interactions Documentation

## Architecture Overview

### 1. InteractionAsset
A ScriptableObject defining an interaction:
- **Roles:** List of `InteractionRole`s. Each role specifies its name, `AnimationClip`, relative slot offset, yaw, and blend times.
- **Anchor:** The frame of reference. `Role` uses one participant's world position as the origin; `World` uses a designated world pose.
- **Alignment:** How characters reach their slots before playing (`Glide`, `Warp`, or `None`).
- **Events:** Timed markers containing actions executed at specific points during playback.

### 2. InteractionActor
Attached to any GameObject representing an interactable character with an `Animator`.
- Discovers movement adapters (`IInteractionMover`) automatically (Transform, CharacterController, NavMeshAgent, Rigidbody).
- Dispatches start and end notifications to `IInteractionParticipant` components.
- Tracks active `InteractionHandle` and busy state.

### 3. Execution & Sync
`Interaction.Play(asset, cast)` instantiates an `InteractionHandle` and registers it with the internal `InteractionRunner`.
- Each participant gets an `AnimationClipPlayable` connected to an `AnimationPlayableOutput`.
- All graphs are advanced on the same frame from a unified master clock, preventing drift.
- When finished or cancelled, all playables are destroyed and the actor's original animator state and mover settings are restored.

### 4. Discovery System
- `InteractionQuery`: Evaluates eligible interaction partners using distance, FOV cone, line of sight, and `InteractionCondition` filters.
- `InteractionTrigger`: Detects targets in real-time, displays interaction prompts, and triggers interactions on key press.
- `IInteractionVeto`: Allows gameplay scripts to dynamically forbid interactions when appropriate.
