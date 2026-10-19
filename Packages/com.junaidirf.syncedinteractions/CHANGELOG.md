# Changelog

All notable changes to the "Synced Interactions" package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
