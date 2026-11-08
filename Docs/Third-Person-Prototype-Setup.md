# Phase 2A: Third-person collision controller

Scope: Windows PC + Photon Fusion 2.1.3 Host/Client prototype.

## Upgrade existing generated assets

1. Fetch and check out this PR branch only when ready for a local compilation test.
2. Exit Play Mode and wait for Unity to compile.
3. Use **Tools > MuOnBaAr > Upgrade Existing Prototype Rider (Third Person)**.
4. Confirm the upgrade; the tool edits only the existing
   `Assets/MuOnBaAr/PrototypeGenerated/NetworkRiderPlayer.prefab`.
5. The prefab root gains Unity `CharacterController` (2m capsule, 0.35m radius).
   The placeholder visual is moved up by 1m to align with the standing capsule.
   The scene itself is not overwritten. Back up or commit existing asset changes first.
6. Check Console, then run the existing prototype scene as Host and connect the
   Windows Client built from the **same source revision**.

## Test

- WASD / standard `Horizontal` and `Vertical` axes (left gamepad stick with
  Unity's default legacy input mapping) move relative to the local camera yaw.
- Hold **right mouse button** to orbit the camera; release it to unlock cursor.
  **Escape** also unlocks the cursor.
- Test moving against a static Cube with a BoxCollider; the rider must not pass
  through it. Place a temporary obstacle in the scene before building both players.
- Press **Tab** on Host and Client: form changes must remain synchronized.
- Both windows should follow **only their locally controlled Rider**.
- Confirm falling/grounding on the Plane, form speed multiplier, and no Console errors.

## Design limits

- Server-authoritative CharacterController with NetworkTransform replication.
- Movement is intentionally **not client-predicted** in this first collision test.
  This avoids non-networked CharacterController state being incorrectly
  resimulated on remote clients, but client input can feel delayed.
- Local-only camera; no Camera or cursor state transmitted over Fusion.
- Mouse look implemented; gamepad right-stick camera bindings are a later task.
- Target Lock, jump, dodge, combat, wall climb and full Fusion KCC still pending.
- No changes to Fusion App ID, NetworkProjectConfig or README.md.
