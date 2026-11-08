# Fusion 2.1.3 — Two-player form sync prototype

This is a temporary Windows PC test, not the production lobby/combat framework.

## Prerequisites

- Unity 2022.3.62f3, Fusion 2.1.3 and a valid Fusion 2 App ID.
- The base MuOnBaAr Runtime assembly and its seven passing EditMode tests.
- Legacy input or Both enabled under Player Settings > Active Input Handling.

## Create the prototype

After importing this branch, choose **Tools > MuOnBaAr > Create Fusion Prototype Scene**.
The command requests confirmation, does not overwrite existing generated assets,
and creates a fresh scene at `Assets/Scenes/MuOnBaArPrototype.unity`.
The scene is added to Build Settings. Generated ScriptableObjects and a network
player prefab are placed under `Assets/MuOnBaAr/PrototypeGenerated`.

Do not place any Photon App ID or API secrets in a commit.

## Test with two instances

1. Let Unity compile. Check Console for errors.
2. Open `MuOnBaArPrototype` and enter Play Mode.
3. Press **HOST** with the default room name in the Editor.
4. Build the same scene for Windows x64, launch the build, and press **JOIN**
   using exactly the same room name and Photon App ID.
5. Use **WASD** to move. Press **Tab** to cycle between two forms.
6. Confirm the placeholder color and form state change on both clients;
   the local HUD displays Form, HP and Energy.
7. Confirm repeated Tab presses obey the cooldown and energy cost.

If the client cannot join, inspect the Console on both clients. The same
region, App ID, room name and available PlayerCount are required.

## Technical boundaries

- Host-authoritative prototype (maximum 2 players by default; configurable).
- Movement uses a simple world-space transform delta with NetworkTransform;
  no gravity, collision-aware KCC, or lag compensation yet.
- Form changes reuse the tested `FormChangeRules` logic on State Authority.
- Cosmetic prefabs are local render-only instances, not network objects.
- Placeholder colors are used when no visual prefab is assigned.
- No production authentication, matchmaking, combat or game-result writes.
- No changes to PhotonAppSettings, NetworkProjectConfig, or README.md.

## Reference

https://doc.photonengine.com/fusion/v2/tutorials/host-mode-basics/2-setting-up-a-scene
