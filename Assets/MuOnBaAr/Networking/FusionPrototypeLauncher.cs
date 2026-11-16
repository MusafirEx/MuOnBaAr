using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using MuOnBaAr.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace MuOnBaAr.Networking
{
    // Temporary host/client launcher for a 2-player PC prototype.
    // Replace the OnGUI controls when the real lobby is implemented.
    public sealed class FusionPrototypeLauncher : MonoBehaviour, INetworkRunnerCallbacks
    {
        [SerializeField] private NetworkObject playerPrefab;
        [SerializeField] private string roomName = "MuOnBaArPrototype";
        [SerializeField] private string fixedPhotonRegion = "asia";
        [SerializeField, Range(2, 10)] private int maximumPlayers = 2;
        [SerializeField] private float playerSpacing = 3f;
        [SerializeField] private bool enableSessionDebugLogs = true;
        // Identifies this MuOnBaAr Fusion 2 application even if the
        // project's global PhotonAppSettings asset has no Fusion App ID.
        private const string DefaultFusionAppId = "299e63d0-0498-408e-8c95-0dba1d14710b";
        [SerializeField] private string fusionAppId = DefaultFusionAppId;

        private readonly Dictionary<PlayerRef, NetworkObject> players =
            new Dictionary<PlayerRef, NetworkObject>();
        private NetworkRunner runner;
        private bool starting;
        private bool pendingFormChange;
        private bool pendingFormConfirm;
        private int requestedFormIndex;
        private int selectedFormIndex = -1;
        private int lastCurrentFormIndex = -1;
        private NetworkRiderController selectionRider;
        private float nextSelectionRiderLookup;
        [SerializeField, Range(0.05f, 0.3f)] private float formScrollRepeatGuardSeconds = 0.15f;
        private float nextFormScrollAllowedTime;
        private bool pendingBasicAttack;
        private bool pendingJump;
        private bool pendingSkill0;
        private bool pendingSkill1;
        private bool pendingSkill2;
        [Header("Double Tap WASD Dodge")]
        [SerializeField, Range(0.1f, 0.5f)] private float doubleTapWindowSeconds = 0.25f;
        private readonly float[] previousTapTimes = { -10f, -10f, -10f, -10f };
        private int pendingDodge = -1;
        private string status = "Not connected";
        private bool sessionConnected;

        public bool IsSessionConnected => sessionConnected;
        public bool IsConnecting => starting;
        public string SessionStatus => status;
        public string InitialRoomName => roomName;
        public bool HasFormPreview => selectionRider != null &&
            selectionRider.Health > 0 && selectionRider.FormCount > 1;
        public string FormPreviewText
        {
            get
            {
                if (!HasFormPreview) return "";
                int slot = selectedFormIndex >= 0 ? selectedFormIndex :
                    selectionRider.CurrentFormIndex;
                string name = selectionRider.GetFormName(slot);
                string marker = !selectionRider.IsFormLevelUnlocked(slot)
                    ? "LOCKED - LEVEL " + selectionRider.GetFormRequiredLevel(slot)
                    : slot == selectionRider.CurrentFormIndex ? "ACTIVE" : "SELECTED";
                return "FORM " + (slot + 1) + "/" + selectionRider.FormCount +
                    "  " + name + "  [" + marker + "]\n" +
                    "Mouse Wheel: Select   |   Middle Click: Confirm";
            }
        }

        private void Start()
        {
            // The Canvas is a scene-authored UI, created through MCP2UNITY.
            // Fallback OnGUI stays available when upgrading older scenes.
            GameObject canvas = GameObject.Find("MuOnBaAr HUD Canvas");
            if (canvas == null)
                return;

            MuOnBaArCanvasHud hud = gameObject.AddComponent<MuOnBaArCanvasHud>();
            if (!hud.Initialize(this, canvas.transform))
                Destroy(hud);
        }

        public void ConnectFromCanvas(bool host, string requestedRoom)
        {
            if (starting || runner != null)
                return;

            if (!string.IsNullOrWhiteSpace(requestedRoom))
                roomName = requestedRoom.Trim();

            // The HOST/JOIN click must not become a buffered combat attack.
            pendingBasicAttack = false;
            pendingJump = false;
            pendingFormChange = false;
            pendingFormConfirm = false;
            selectedFormIndex = lastCurrentFormIndex = -1;
            selectionRider = null;
            nextFormScrollAllowedTime = 0f;
            pendingSkill0 = pendingSkill1 = pendingSkill2 = false;
            StartSession(host ? GameMode.Host : GameMode.Client);
        }

        private void Update()
        {
            // Latch one-frame button presses until Fusion consumes an input tick.
            bool pointerOverUi = EventSystem.current != null &&
                                 EventSystem.current.IsPointerOverGameObject();
            Camera cam = Camera.main;
            RiderThirdPersonCamera orbit = cam != null
                ? cam.GetComponent<RiderThirdPersonCamera>() : null;
            bool gameplay = sessionConnected && orbit != null &&
                orbit.Target != null && orbit.IsPointerCaptured && Application.isFocused;
            UpdateLocalFormSelectionRider();
            // No attacks when UI is active or when the first click recaptures mouse.
            if (!gameplay || pointerOverUi)
                return;
            pendingFormChange |= Input.GetKeyDown(KeyCode.Tab);
            if (HasFormPreview)
            {
                float scroll = Input.mouseScrollDelta.y;
                // A physical wheel notch can generate several non-zero frames.
                // Accept only one step per short burst, regardless of delta size.
                if (Mathf.Abs(scroll) > 0.01f &&
                    Time.unscaledTime >= nextFormScrollAllowedTime)
                {
                    nextFormScrollAllowedTime = Time.unscaledTime +
                        formScrollRepeatGuardSeconds;
                    int direction = scroll > 0f ? -1 : 1;
                    int count = selectionRider.FormCount;
                    int currentSelection = selectedFormIndex >= 0
                        ? selectedFormIndex : selectionRider.CurrentFormIndex;
                    selectedFormIndex = (currentSelection + direction + count) % count;
                }
                if (Input.GetMouseButtonDown(2))
                {
                    int choice = selectedFormIndex >= 0 ? selectedFormIndex :
                        selectionRider.CurrentFormIndex;
                    if (choice != selectionRider.CurrentFormIndex &&
                        selectionRider.IsFormLevelUnlocked(choice))
                    {
                        requestedFormIndex = choice;
                        pendingFormConfirm = true;
                    }
                }
            }
            pendingBasicAttack |= Input.GetMouseButtonDown(0);
            pendingJump |= Input.GetKeyDown(KeyCode.Space);
            pendingSkill0 |= Input.GetMouseButtonDown(1);
            pendingSkill1 |= Input.GetKeyDown(KeyCode.E);
            pendingSkill2 |= Input.GetKeyDown(KeyCode.R);
            CheckDoubleTap(KeyCode.W, 0);
            CheckDoubleTap(KeyCode.A, 1);
            CheckDoubleTap(KeyCode.S, 2);
            CheckDoubleTap(KeyCode.D, 3);
        }

        private void CheckDoubleTap(KeyCode key, int direction)
        {
            if (!Input.GetKeyDown(key)) return;
            float now = Time.unscaledTime;
            if (now - previousTapTimes[direction] <= doubleTapWindowSeconds)
            {
                pendingDodge = direction;
                previousTapTimes[direction] = -10f;
            }
            else previousTapTimes[direction] = now;
        }

        private void UpdateLocalFormSelectionRider()
        {
            if (selectionRider == null || selectionRider.Object == null ||
                !selectionRider.Object.HasInputAuthority)
            {
                if (Time.unscaledTime < nextSelectionRiderLookup)
                    return;
                nextSelectionRiderLookup = Time.unscaledTime + 0.25f;
                selectionRider = null;
                NetworkRiderController[] riders =
                    FindObjectsOfType<NetworkRiderController>();
                foreach (NetworkRiderController rider in riders)
                {
                    if (rider != null && rider.Object != null &&
                        rider.Object.HasInputAuthority)
                    {
                        selectionRider = rider;
                        selectedFormIndex = rider.CurrentFormIndex;
                        lastCurrentFormIndex = rider.CurrentFormIndex;
                        break;
                    }
                }
            }
            if (selectionRider != null &&
                lastCurrentFormIndex != selectionRider.CurrentFormIndex)
            {
                lastCurrentFormIndex = selectionRider.CurrentFormIndex;
                selectedFormIndex = lastCurrentFormIndex;
            }
        }

        private async void StartSession(GameMode mode)
        {
            if (starting || runner != null)
                return;

            if (playerPrefab == null)
            {
                status = "Assign a player NetworkObject prefab.";
                Debug.LogError(status, this);
                return;
            }

            string appId = string.IsNullOrWhiteSpace(fusionAppId)
                ? DefaultFusionAppId : fusionAppId.Trim();
            if (!Guid.TryParse(appId, out _))
            {
                status = "Invalid Fusion App ID. Check Fusion Session Launcher.";
                Debug.LogError("[MuOnBaAr][Fusion] " + status, this);
                return;
            }

            int sceneIndex = SceneManager.GetActiveScene().buildIndex;
            if (sceneIndex < 0)
            {
                status = "Add the current scene to Build Settings first.";
                Debug.LogError(status, this);
                return;
            }

            starting = true;
            status = "Connecting to Photon...";
            runner = gameObject.AddComponent<NetworkRunner>();
            runner.ProvideInput = true;

            // The runner discovers INetworkRunnerCallbacks beside it.
            var sceneRef = SceneRef.FromIndex(sceneIndex);
            var networkScene = new NetworkSceneInfo();
            networkScene.AddSceneRef(sceneRef, LoadSceneMode.Additive);

            try
            {
                // Fusion 2 allows per-runner App ID configuration. Use a copy
                // of the global transport settings; never modify the asset.
                var cloudSettings = Fusion.Photon.Realtime.PhotonAppSettings.Global
                    .AppSettings.GetCopy();
                cloudSettings.AppIdFusion = appId;
                cloudSettings.UseNameServer = true;
                // Both HOST and JOIN must resolve the same Cloud region.
                // Auto best-region selection can silently put players in different rooms.
                cloudSettings.FixedRegion = string.IsNullOrWhiteSpace(fixedPhotonRegion)
                    ? "asia" : fixedPhotonRegion.Trim().ToLowerInvariant();
                Debug.Log("[MuOnBaAr][Fusion] Connecting with explicit Fusion App ID " +
                    "(value hidden); region=" +
                    (string.IsNullOrEmpty(cloudSettings.FixedRegion)
                        ? "Auto" : cloudSettings.FixedRegion) +
                    "; mode=" + mode, this);

                var startArgs = new StartGameArgs
                {
                    CustomPhotonAppSettings = cloudSettings,
                    GameMode = mode,
                    SessionName = roomName.Trim(),
                    PlayerCount = maximumPlayers,
                    Scene = networkScene,
                    SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
                };

                StartGameResult result = await runner.StartGame(startArgs);
                sessionConnected = result.Ok;
                string region = cloudSettings.FixedRegion;
                status = result.Ok
                    ? (mode + " connected: " + roomName + " [" + region + "]")
                    : ("Connection failed [" + region + "] room=" + roomName + ": " + result);
                Debug.Log("[MuOnBaAr][Fusion] session " + (result.Ok ? "READY" : "FAILED") +
                    " mode=" + mode + " room=" + roomName + " region=" + region +
                    " status=" + result, this);

                if (!result.Ok)
                {
                    Destroy(runner);
                    runner = null;
                }
            }
            catch (Exception exception)
            {
                sessionConnected = false;
                status = "Connection error: " + exception.Message;
                Debug.LogException(exception, this);
                if (runner != null)
                {
                    Destroy(runner);
                    runner = null;
                }
            }
            finally
            {
                starting = false;
            }
        }

        private void OnGUI()
        {
            if (MuOnBaArCanvasHud.IsReady)
                return;

            GUI.Box(new Rect(10f, 10f, 380f, 156f), "MuOnBaAr Fusion Prototype");

            if (runner == null && !starting)
            {
                GUI.Label(new Rect(20f, 37f, 90f, 22f), "Room");
                roomName = GUI.TextField(
                    new Rect(114f, 37f, 262f, 24f), roomName, 32);
                if (GUI.Button(new Rect(20f, 73f, 170f, 32f), "HOST"))
                    StartSession(GameMode.Host);
                if (GUI.Button(new Rect(200f, 73f, 176f, 32f), "JOIN"))
                    StartSession(GameMode.Client);
            }

            GUI.Label(new Rect(20f, 117f, 360f, 38f), status);
        }

        public void OnPlayerJoined(NetworkRunner networkRunner, PlayerRef player)
        {
            if (!networkRunner.IsServer || players.ContainsKey(player))
                return;

            Vector3 spawn = new Vector3(players.Count * playerSpacing, 1f, 0f);
            NetworkObject avatar = networkRunner.Spawn(
                playerPrefab, spawn, Quaternion.identity, player);

            if (avatar == null)
            {
                Debug.LogError("Failed to spawn the network rider prefab.", this);
                return;
            }

            players.Add(player, avatar);
            networkRunner.SetPlayerObject(player, avatar);
            if (enableSessionDebugLogs)
                Debug.Log(RiderCombatTelemetry.Format(
                    "JOIN", networkRunner.Tick.Raw, player.ToString(),
                    avatar.Id.ToString(), "spawn=" + spawn.ToString("F2")), this);
        }

        public void OnPlayerLeft(NetworkRunner networkRunner, PlayerRef player)
        {
            if (!networkRunner.IsServer)
                return;

            if (players.TryGetValue(player, out NetworkObject avatar))
            {
                if (enableSessionDebugLogs)
                    Debug.Log(RiderCombatTelemetry.Format(
                        "LEAVE", networkRunner.Tick.Raw, player.ToString(),
                        avatar != null ? avatar.Id.ToString() : "none",
                        "reason=PLAYER_LEFT"), this);
                players.Remove(player);
                if (avatar != null)
                    networkRunner.Despawn(avatar);
            }
        }

        public void OnInput(NetworkRunner networkRunner, NetworkInput input)
        {
            // Unity's legacy Horizontal/Vertical axes support WASD and common
            // gamepad left sticks. Camera-relative input is computed locally.
            Vector2 move = new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical"));

            Camera camera = Camera.main;
            float yaw = camera != null ? camera.transform.eulerAngles.y : 0f;
            if (camera != null)
            {
                RiderThirdPersonCamera follow = camera.GetComponent<RiderThirdPersonCamera>();
                if (follow != null && follow.Target != null)
                    yaw = follow.Yaw;
            }

            float yawRadians = yaw * Mathf.Deg2Rad;
            Vector2 aim = new Vector2(Mathf.Sin(yawRadians), Mathf.Cos(yawRadians));
            if (camera != null)
            {
                RiderThirdPersonCamera follow = camera.GetComponent<RiderThirdPersonCamera>();
                if (follow != null && follow.Target != null)
                {
                    NetworkRiderController localRider =
                        follow.Target.GetComponent<NetworkRiderController>();
                    if (localRider != null)
                        aim = localRider.GetLocalCombatAim(yaw);
                }
            }

            var data = new RiderNetworkInput
            {
                Move = RiderMovementMath.CameraRelativeToWorld(move, yaw),
                Aim = aim
            };
            data.Buttons.Set(RiderInputButton.CycleForm, pendingFormChange);
            data.Buttons.Set(RiderInputButton.ConfirmForm, pendingFormConfirm);
            data.RequestedFormIndex = requestedFormIndex;
            data.Buttons.Set(RiderInputButton.BasicAttack, pendingBasicAttack);
            data.Buttons.Set(RiderInputButton.Jump, pendingJump);
            // Held inputs are sampled every Fusion tick for Flight Forms.
            data.Buttons.Set(RiderInputButton.JumpHeld, Input.GetKey(KeyCode.Space));
            data.Buttons.Set(RiderInputButton.FlyDescendHeld,
                Input.GetKey(KeyCode.LeftAlt));
            data.Buttons.Set(RiderInputButton.Skill0, pendingSkill0);
            data.Buttons.Set(RiderInputButton.Skill1, pendingSkill1);
            data.Buttons.Set(RiderInputButton.Skill2, pendingSkill2);
            data.Buttons.Set(RiderInputButton.DodgeForward, pendingDodge == 0);
            data.Buttons.Set(RiderInputButton.DodgeLeft, pendingDodge == 1);
            data.Buttons.Set(RiderInputButton.DodgeBack, pendingDodge == 2);
            data.Buttons.Set(RiderInputButton.DodgeRight, pendingDodge == 3);
            // Continuous held input; never trust client-reported charge duration.
            data.Buttons.Set(RiderInputButton.Skill0Held, Input.GetMouseButton(1));
            data.Buttons.Set(RiderInputButton.Skill1Held, Input.GetKey(KeyCode.E));
            data.Buttons.Set(RiderInputButton.Skill2Held, Input.GetKey(KeyCode.R));
            // Held sprint is sampled each Fusion tick, unlike one-shot actions.
            data.Buttons.Set(RiderInputButton.Sprint,
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            pendingFormChange = false;
            pendingFormConfirm = false;
            pendingBasicAttack = false;
            pendingJump = false;
            pendingSkill0 = pendingSkill1 = pendingSkill2 = false;
            pendingDodge = -1;
            input.Set(data);
        }

        public void OnShutdown(NetworkRunner networkRunner, ShutdownReason shutdownReason)
        {
            sessionConnected = false;
            players.Clear();
            selectionRider = null;
            selectedFormIndex = lastCurrentFormIndex = -1;
            pendingFormChange = pendingFormConfirm = false;
            status = "Disconnected: " + shutdownReason;
            runner = null;
            starting = false;
        }

        public void OnConnectedToServer(NetworkRunner networkRunner) { }
        public void OnDisconnectedFromServer(NetworkRunner networkRunner, NetDisconnectReason reason)
        {
            sessionConnected = false;
            status = "Disconnected: " + reason;
        }
        public void OnConnectRequest(NetworkRunner networkRunner,
            NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner networkRunner,
            NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            sessionConnected = false;
            status = "Connect failed: " + reason;
        }
        public void OnInputMissing(NetworkRunner networkRunner, PlayerRef player,
            NetworkInput input) { }
        // Fusion 2.1.3 requires this callback signature but marks the
        // parameter type obsolete. Limit the warning suppression to the
        // mandatory INetworkRunnerCallbacks implementation.
#pragma warning disable CS0618
        public void OnUserSimulationMessage(NetworkRunner networkRunner,
            SimulationMessagePtr message) { }
#pragma warning restore CS0618
        public void OnSessionListUpdated(NetworkRunner networkRunner,
            List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner networkRunner,
            Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner networkRunner,
            HostMigrationToken hostMigrationToken) { }
        public void OnSceneLoadDone(NetworkRunner networkRunner) { }
        public void OnSceneLoadStart(NetworkRunner networkRunner) { }
        public void OnObjectExitAOI(NetworkRunner networkRunner,
            NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner networkRunner,
            NetworkObject obj, PlayerRef player) { }
        public void OnReliableDataProgress(NetworkRunner networkRunner,
            PlayerRef player, ReliableKey key, float progress) { }
        public void OnReliableDataReceived(NetworkRunner networkRunner,
            PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    }
}
