using Fusion;
using MuOnBaAr.Runtime;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MuOnBaAr.Networking
{
    /// <summary>
    /// Local-only uGUI controller. No combat or network state is modified.
    /// Bindings are by child name so designers can edit the scene Canvas.
    /// </summary>
    public sealed class MuOnBaArCanvasHud : MonoBehaviour
    {
        private static MuOnBaArCanvasHud activeHud;
        private FusionPrototypeLauncher launcher;
        private GameObject connectionPanel;
        private GameObject connectedPanel;
        private GameObject combatPanel;
        private InputField roomInput;
        private Button hostButton;
        private Button joinButton;
        private Text connectionStatus;
        private Text connectedStatus;
        private Image characterIcon;
        private Image healthBar;
        private Image energyBar;
        private Image levelBar;
        private TMP_Text healthLabel;
        private TMP_Text energyLabel;
        private TMP_Text levelLabel;
        private TMP_Text currentFormName;
        private Text formSelectionPreview;
        private NetworkRiderController localRider;
        private float nextRiderSearch;
        private bool initialized;

        public static bool IsReady =>
            activeHud != null && activeHud.initialized && activeHud.isActiveAndEnabled;

        public bool Initialize(FusionPrototypeLauncher owner, Transform canvasRoot)
        {
            if (owner == null || canvasRoot == null)
                return false;

            launcher = owner;
            connectionPanel = FindObject(canvasRoot, "ConnectionPanel");
            connectedPanel = FindObject(canvasRoot, "ConnectedPanel");
            combatPanel = FindObject(canvasRoot, "CombatPanel");
            roomInput = FindComponent<InputField>(canvasRoot, "ConnectionPanel/RoomInput");
            hostButton = FindComponent<Button>(canvasRoot, "ConnectionPanel/HostButton");
            joinButton = FindComponent<Button>(canvasRoot, "ConnectionPanel/JoinButton");
            connectionStatus = FindComponent<Text>(canvasRoot, "ConnectionPanel/ConnectionStatus");
            connectedStatus = FindComponent<Text>(canvasRoot, "ConnectedPanel/ConnectedText");
            characterIcon = FindComponent<Image>(canvasRoot, "CombatPanel/CharIcon");
            // Retain scene-authored objects; do not display legacy debug text.
            GameObject legacyStats = FindObject(canvasRoot, "CombatPanel/RiderStats");
            GameObject legacyLock = FindObject(canvasRoot, "CombatPanel/LockInfo");
            GameObject legacyState = FindObject(canvasRoot, "CombatPanel/CombatState");
            if (legacyStats != null) legacyStats.SetActive(false);
            if (legacyLock != null) legacyLock.SetActive(false);
            if (legacyState != null) legacyState.SetActive(false);

            if (connectionPanel == null || connectedPanel == null || combatPanel == null ||
                roomInput == null || hostButton == null || joinButton == null ||
                connectionStatus == null || connectedStatus == null ||
                characterIcon == null)
            {
                Debug.LogError("[MuOnBaAr][UI] Canvas hierarchy incomplete. Legacy OnGUI kept.", owner);
                return false;
            }

            roomInput.text = launcher.InitialRoomName;
            hostButton.onClick.AddListener(HostClicked);
            joinButton.onClick.AddListener(JoinClicked);
            ConfigureText(connectionStatus, 15);
            ConfigureText(connectedStatus, 15);
            BindCombatBars(canvasRoot);
            if (healthBar == null || energyBar == null || levelBar == null ||
                healthLabel == null || energyLabel == null || levelLabel == null)
            {
                Debug.LogError("[MuOnBaAr][UI] Missing scene-authored CombatPanel " +
                    "HealthBar/EnergyBar/LevelBar (Fill and Label required).", owner);
                return false;
            }
            SetupFormSelectionPreview(canvasRoot);
            ConfigureButtonText(hostButton);
            ConfigureButtonText(joinButton);
            initialized = true;
            activeHud = this;
            RefreshUi();

            Debug.Log("[WDMCP_EVENT][MuOnBaAr][UI] event=HUD_READY mode=Canvas " +
                "panels=3 buttons=2", owner);
            return true;
        }

        private void BindCombatBars(Transform canvasRoot)
        {
            // All three bars are permanent scene-authored UI elements.
            // Never generate runtime replacements or move the user's CharIcon.
            healthBar = FindComponent<Image>(canvasRoot, "CombatPanel/HealthBar/Fill");
            energyBar = FindComponent<Image>(canvasRoot, "CombatPanel/EnergyBar/Fill");
            levelBar = FindComponent<Image>(canvasRoot, "CombatPanel/LevelBar/Fill");
            healthLabel = FindComponent<TMP_Text>(canvasRoot, "CombatPanel/HealthBar/Label");
            energyLabel = FindComponent<TMP_Text>(canvasRoot, "CombatPanel/EnergyBar/Label");
            levelLabel = FindComponent<TMP_Text>(canvasRoot, "CombatPanel/LevelBar/Label");
            currentFormName = FindComponent<TMP_Text>(canvasRoot, "CombatPanel/Form/Current Form Name");
        }

        private static void ResizeBar(Image fill, float value)
        {
            if (fill == null) return;
            RectTransform rect = fill.rectTransform;
            // Stretch horizontally from the left of its authored parent.
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void SetupFormSelectionPreview(Transform canvasRoot)
        {
            // Debug-only Canvas label, created at runtime; does not change scene assets.
            GameObject labelObject = new GameObject("FormSelectionPreview",
                typeof(RectTransform), typeof(Text));
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.SetParent(canvasRoot, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-22f, -20f);
            rect.sizeDelta = new Vector2(520f, 72f);
            formSelectionPreview = labelObject.GetComponent<Text>();
            formSelectionPreview.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            formSelectionPreview.fontSize = 20;
            formSelectionPreview.fontStyle = FontStyle.Bold;
            formSelectionPreview.alignment = TextAnchor.UpperRight;
            formSelectionPreview.color = new Color(1f, 0.48f, 0.08f);
            formSelectionPreview.horizontalOverflow = HorizontalWrapMode.Wrap;
            formSelectionPreview.verticalOverflow = VerticalWrapMode.Overflow;
            formSelectionPreview.raycastTarget = false;
            labelObject.SetActive(false);
        }

        private static GameObject FindObject(Transform root, string path)
        {
            Transform child = root.Find(path);
            return child != null ? child.gameObject : null;
        }

        private static T FindComponent<T>(Transform root, string path)
            where T : Component
        {
            Transform child = root.Find(path);
            return child != null ? child.GetComponent<T>() : null;
        }

        private static void ConfigureText(Text label, int fontSize)
        {
            label.alignment = TextAnchor.MiddleLeft;
            // Do not overwrite the scene-authored muted/orange HUD colors.
            label.fontSize = fontSize;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private static void ConfigureButtonText(Button button)
        {
            Text label = button.GetComponentInChildren<Text>(true);
            if (label == null)
                return;

            label.color = Color.white;
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
        }

        private void HostClicked()
        {
            if (launcher != null)
                launcher.ConnectFromCanvas(true, roomInput.text);
        }

        private void JoinClicked()
        {
            if (launcher != null)
                launcher.ConnectFromCanvas(false, roomInput.text);
        }

        private void Update()
        {
            if (initialized && launcher != null)
                RefreshUi();
        }

        private void RefreshUi()
        {
            bool connected = launcher.IsSessionConnected;
            bool waiting = launcher.IsConnecting;
            if (connectionPanel.activeSelf == connected)
                connectionPanel.SetActive(!connected);
            if (connectedPanel.activeSelf != connected)
                connectedPanel.SetActive(connected);

            hostButton.interactable = !waiting;
            joinButton.interactable = !waiting;
            connectionStatus.text = launcher.SessionStatus;
            connectedStatus.text = launcher.SessionStatus;

            if (formSelectionPreview != null)
            {
                bool visible = connected && launcher.HasFormPreview;
                if (formSelectionPreview.gameObject.activeSelf != visible)
                    formSelectionPreview.gameObject.SetActive(visible);
                if (visible)
                    formSelectionPreview.text = launcher.FormPreviewText;
            }

            if (!connected)
            {
                if (combatPanel.activeSelf)
                    combatPanel.SetActive(false);
                localRider = null;
                return;
            }

            if (localRider == null || localRider.Object == null ||
                !localRider.Object.HasInputAuthority)
            {
                if (Time.unscaledTime >= nextRiderSearch)
                {
                    nextRiderSearch = Time.unscaledTime + 0.25f;
                    localRider = FindLocalRider();
                }
            }

            if (localRider == null)
            {
                if (combatPanel.activeSelf)
                    combatPanel.SetActive(false);
                return;
            }

            if (!combatPanel.activeSelf)
                combatPanel.SetActive(true);

            characterIcon.sprite = localRider.CharacterIcon;
            characterIcon.enabled = characterIcon.sprite != null;
            ResizeBar(healthBar, (float)localRider.Health / Mathf.Max(1, localRider.MaxHealth));
            ResizeBar(energyBar, (float)localRider.Energy / Mathf.Max(1, localRider.MaxEnergy));
            int neededXp = localRider.ExperienceToNextLevel;
            ResizeBar(levelBar, neededXp == 0 ? 1f :
                (float)localRider.Experience / neededXp);
            healthLabel.text = "HP  " + localRider.Health + "/" + localRider.MaxHealth;
            energyLabel.text = "ENERGY  " + localRider.Energy + "/" + localRider.MaxEnergy;
            if (currentFormName != null)
                currentFormName.text = localRider.HudFormName;
            levelLabel.text = neededXp == 0
                ? "LEVEL " + localRider.Level + " MAX"
                : "LEVEL " + localRider.Level + "  XP " + localRider.Experience +
                    "/" + neededXp;
        }

        private static NetworkRiderController FindLocalRider()
        {
            NetworkRiderController[] riders =
                FindObjectsOfType<NetworkRiderController>();
            for (int i = 0; i < riders.Length; i++)
            {
                NetworkRiderController candidate = riders[i];
                if (candidate != null && candidate.Object != null &&
                    candidate.Object.HasInputAuthority)
                    return candidate;
            }
            return null;
        }

        private void OnDestroy()
        {
            if (hostButton != null)
                hostButton.onClick.RemoveListener(HostClicked);
            if (joinButton != null)
                joinButton.onClick.RemoveListener(JoinClicked);
            if (activeHud == this)
                activeHud = null;
        }
    }
}
