# Basic Two-Role Sample

This sample demonstrates a clean two-character synced interaction (e.g. a handshake):
- Two primitive humanoid characters with `InteractionActor`.
- An `InteractionAsset` configured with two roles (`Initiator` and `Receiver`).
- An `InteractionTrigger` component on the player character with key `[E]`.
- As you approach the partner within 2 meters, the prompt appears; pressing `[E]` glides the characters into their slots and plays the synced interaction.

To use in your own project:
1. Import this sample from the Package Manager window.
2. Open the sample scene.
3. Press Play and walk up to the partner character.
