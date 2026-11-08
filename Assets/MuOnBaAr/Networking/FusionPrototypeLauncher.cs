using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using MuOnBaAr.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MuOnBaAr.Networking
{
    // Temporary host/client launcher for a 2-player PC prototype.
    // Replace the OnGUI controls when the real lobby is implemented.
    public sealed class FusionPrototypeLauncher : MonoBehaviour, INetworkRunnerCallbacks
    {
        [SerializeField] private NetworkObject playerPrefab;
        [SerializeField] private string roomName = "MuOnBaArPrototype";
        [SerializeField, Range(2, 10)] private int maximumPlayers = 2;
        [SerializeField] private float playerSpacing = 3f;

        private readonly Dictionary<PlayerRef, NetworkObject> players =
            new Dictionary<PlayerRef, NetworkObject>();
        private NetworkRunner runner;
        private bool starting;
        private bool pendingFormChange;
        private string status = "Not connected";

        private void Update()
        {
            // Latch one-frame button presses until Fusion consumes an input tick.
            pendingFormChange |= Input.GetKeyDown(KeyCode.Tab);
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
                var startArgs = new StartGameArgs
                {
                    GameMode = mode,
                    SessionName = roomName.Trim(),
                    PlayerCount = maximumPlayers,
                    Scene = networkScene,
                    SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
                };

                StartGameResult result = await runner.StartGame(startArgs);
                status = result.Ok ? (mode + " connected: " + roomName)
                                   : ("Connection failed: " + result);

                if (!result.Ok)
                {
                    Destroy(runner);
                    runner = null;
                }
            }
            catch (Exception exception)
            {
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
        }

        public void OnPlayerLeft(NetworkRunner networkRunner, PlayerRef player)
        {
            if (!networkRunner.IsServer)
                return;

            if (players.TryGetValue(player, out NetworkObject avatar))
            {
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

            var data = new RiderNetworkInput
            {
                Move = RiderMovementMath.CameraRelativeToWorld(move, yaw)
            };
            data.Buttons.Set(RiderInputButton.CycleForm, pendingFormChange);
            pendingFormChange = false;
            input.Set(data);
        }

        public void OnShutdown(NetworkRunner networkRunner, ShutdownReason shutdownReason)
        {
            players.Clear();
            status = "Disconnected: " + shutdownReason;
            runner = null;
            starting = false;
        }

        public void OnConnectedToServer(NetworkRunner networkRunner) { }
        public void OnDisconnectedFromServer(NetworkRunner networkRunner, NetDisconnectReason reason)
        {
            status = "Disconnected: " + reason;
        }
        public void OnConnectRequest(NetworkRunner networkRunner,
            NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner networkRunner,
            NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            status = "Connect failed: " + reason;
        }
        public void OnInputMissing(NetworkRunner networkRunner, PlayerRef player,
            NetworkInput input) { }
        public void OnUserSimulationMessage(NetworkRunner networkRunner,
            SimulationMessagePtr message) { }
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
