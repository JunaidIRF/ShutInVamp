# Changelog

All notable changes to the "Synced Interactions" package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-10-19

### Added
- **Dual Rig Authoring Mode ("Animate Both")**: Edit and keyframe both characters simultaneously on a unified dopesheet in Unity's standard Animation Window.
- **Dynamic Hierarchy Resolver**: Automatically identifies animated roots when `Animator` components reside on child GameObjects, eliminating missing curve bindings (`(Missing!)`).
- **Bi-directional Event Synchronization**: Dual Rig animation dopesheet event markers synchronize directly with `InteractionAsset.events`, matching actions and parameters.
- **Object Reference Curves**: Full support for editing and saving ObjectReference curves alongside float curves in dual rig mode.
- **Scene Lifecycle Asset Caching**: Safe handling of ScriptableObject references across scene unloading and opening in stage editors.
- **Comprehensive User Manual**: Added `Documentation~/Manual.md` with Mermaid architecture diagrams, sequence flows, slot specs, and modular custom action implementations.

## [0.1.0] - 2026-10-19

### Added
- Core runtime: `InteractionAsset`, `InteractionActor`, `InteractionHandle`, and `Interaction.Play`.
- Playables-based animation output blending without animator controller modifications.
- Slot alignment system (Glide, Warp, None) with ground raycast snapping.
- Timed interaction events with extensible `[SerializeReference] InteractionAction` classes.
- Built-in actions: invoke actor event, play sound, spawn prefab, set active, set layer, release role.
- Timeline authoring stage (`InteractionStage`) with live synchronization, marker dragging, and slot editing.
- Custom property drawers for actions, conditions, bone lookup, and role name selection.
- Partner discovery system: `InteractionQuery`, `InteractionCondition`, and `InteractionTrigger`.
- Veto interface `IInteractionVeto` for character-level interaction rejection.
- Automated EditMode and Runtime tests.
