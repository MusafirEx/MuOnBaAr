using MuOnBaAr.Runtime;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MuOnBaAr.Networking
{
    /// <summary>
    /// Client-side visual UI only. Lives on a World Space Canvas under
    /// NetworkRiderPlayer. The authoritative HP remains on NetworkRiderController.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RiderWorldHealthBar : MonoBehaviour
    {
        [SerializeField] private Slider healthSlider;
        [SerializeField] private Image healthFill;
        [SerializeField] private TextMeshProUGUI playerNameHealth;
        [SerializeField] private Image feedbackFlash;
        [SerializeField] private TextMeshProUGUI damagePopup;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Client-side Visibility")]
        [SerializeField] private bool showLocalPlayerBar = false;
        [SerializeField] private bool avoidScreenHud = true;
        [SerializeField, Min(1f)] private float maximumViewDistance = 35f;
        [SerializeField, Range(0f, 32f)] private float hudMarginPixels = 12f;

        private Canvas overheadCanvas;
        private NetworkRiderController rider;
        private RectTransform healthPanel;
        private Canvas screenHudCanvas;
        private RectTransform connectionPanel;
        private RectTransform connectedPanel;
        private RectTransform combatPanel;
        private readonly Vector3[] healthCorners = new Vector3[4];
        private readonly Vector3[] uiCorners = new Vector3[4];

        public void SetState(
            string playerName, int health, int maximumHealth, bool knockedOut,
            bool respawnProtected, bool showFeedback,
            float lastHitTime, float lastAttackTime, float flashDuration,
            int lastDamageAmount, float popupDuration)
        {
            float fraction = RiderCombatFeedbackRules.HealthFraction(
                health, maximumHealth);

            if (healthSlider != null)
            {
                healthSlider.minValue = 0f;
                healthSlider.maxValue = 1f;
                healthSlider.SetValueWithoutNotify(fraction);
            }

            if (healthFill != null)
                healthFill.color = Color.Lerp(
                    new Color(0.95f, 0.22f, 0.2f),
                    new Color(0.25f, 0.85f, 0.3f),
                    fraction);

            if (playerNameHealth != null)
                playerNameHealth.text = playerName + "  HP " +
                    Mathf.Max(0, health) + "/" + Mathf.Max(1, maximumHealth);

            if (statusText != null)
                statusText.text = knockedOut ? "KO" :
                    respawnProtected ? "RESPAWN SHIELD" : "";

            float now = Time.unscaledTime;
            bool hitFlash = showFeedback &&
                now - lastHitTime >= 0f &&
                now - lastHitTime < flashDuration;
            bool attackFlash = showFeedback && !hitFlash &&
                now - lastAttackTime >= 0f &&
                now - lastAttackTime < flashDuration;

            if (feedbackFlash != null)
            {
                feedbackFlash.enabled = hitFlash || attackFlash;
                if (feedbackFlash.enabled)
                    feedbackFlash.color = hitFlash ? Color.red : Color.yellow;
            }

            if (damagePopup != null)
            {
                float elapsed = now - lastHitTime;
                bool visible = showFeedback && lastDamageAmount > 0 &&
                    elapsed >= 0f && elapsed < popupDuration;
                damagePopup.enabled = visible;
                if (visible)
                {
                    damagePopup.text = "-" + lastDamageAmount;
                    Color color = damagePopup.color;
                    color.a = 1f - Mathf.Clamp01(
                        elapsed / Mathf.Max(0.01f, popupDuration));
                    damagePopup.color = color;
                    damagePopup.rectTransform.anchoredPosition =
                        new Vector2(0f, 55f + elapsed * 25f);
                }
            }
        }

        private void Awake()
        {
            overheadCanvas = GetComponent<Canvas>();
            rider = GetComponentInParent<NetworkRiderController>();
            healthPanel = transform.Find("HealthPanel") as RectTransform;
            FindScreenHud();
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                SetCanvasVisible(false);
                return;
            }

            // The canvas stays parented to its networked rider and faces
            // each client's camera. Visibility never changes network state.
            transform.rotation = camera.transform.rotation;
            if (rider == null)
                rider = GetComponentInParent<NetworkRiderController>();
            if (overheadCanvas == null)
                overheadCanvas = GetComponent<Canvas>();
            if (healthPanel == null)
                healthPanel = transform.Find("HealthPanel") as RectTransform;

            Vector3 projected = camera.WorldToScreenPoint(transform.position);
            bool inFront = projected.z > camera.nearClipPlane;
            bool inViewport = false;
            bool overlapsHud = false;
            if (inFront && healthPanel != null)
            {
                Rect barRect = ScreenRect(healthPanel, camera, healthCorners);
                inViewport = barRect.Overlaps(camera.pixelRect);
                if (inViewport && avoidScreenHud)
                    overlapsHud = OverlapsScreenHud(barRect, camera);
            }

            bool isLocal = rider != null && rider.Object != null &&
                rider.Object.HasInputAuthority;
            float distance = Vector3.Distance(
                camera.transform.position, transform.position);
            SetCanvasVisible(RiderCombatFeedbackRules.ShouldDisplay(
                isLocal, showLocalPlayerBar, inFront, inViewport,
                overlapsHud, distance, maximumViewDistance));
        }

        private void SetCanvasVisible(bool visible)
        {
            // Hide only the Canvas renderer, not this behaviour, so SetState
            // continues to follow the replicated HP on the next update.
            if (overheadCanvas != null && overheadCanvas.enabled != visible)
                overheadCanvas.enabled = visible;
        }

        private void FindScreenHud()
        {
            GameObject hud = GameObject.Find("MuOnBaAr HUD Canvas");
            if (hud == null)
                return;
            screenHudCanvas = hud.GetComponent<Canvas>();
            Transform root = hud.transform;
            connectionPanel = root.Find("ConnectionPanel") as RectTransform;
            connectedPanel = root.Find("ConnectedPanel") as RectTransform;
            combatPanel = root.Find("CombatPanel") as RectTransform;
        }

        private bool OverlapsScreenHud(Rect barRect, Camera gameCamera)
        {
            if (screenHudCanvas == null)
                FindScreenHud();
            if (screenHudCanvas == null)
                return false;

            Camera uiCamera = screenHudCanvas.renderMode ==
                RenderMode.ScreenSpaceOverlay ? null :
                (screenHudCanvas.worldCamera != null ?
                    screenHudCanvas.worldCamera : gameCamera);

            return OverlapsPanel(barRect, connectionPanel, uiCamera) ||
                OverlapsPanel(barRect, connectedPanel, uiCamera) ||
                OverlapsPanel(barRect, combatPanel, uiCamera);
        }

        private bool OverlapsPanel(
            Rect barRect, RectTransform panel, Camera camera)
        {
            if (panel == null || !panel.gameObject.activeInHierarchy)
                return false;

            Rect bounds = ScreenRect(panel, camera, uiCorners);
            bounds.xMin -= hudMarginPixels;
            bounds.xMax += hudMarginPixels;
            bounds.yMin -= hudMarginPixels;
            bounds.yMax += hudMarginPixels;
            return barRect.Overlaps(bounds);
        }

        private static Rect ScreenRect(
            RectTransform rect, Camera camera, Vector3[] corners)
        {
            rect.GetWorldCorners(corners);
            Vector2 first = RectTransformUtility.WorldToScreenPoint(
                camera, corners[0]);
            float minX = first.x, maxX = first.x;
            float minY = first.y, maxY = first.y;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(
                    camera, corners[i]);
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }
    }
}
