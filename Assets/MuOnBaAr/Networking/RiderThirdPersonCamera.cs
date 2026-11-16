using UnityEngine;

namespace MuOnBaAr.Networking
{
    /// <summary>One local, non-networked camera per game instance. Never follows proxies.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class RiderThirdPersonCamera : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float distance = 5f;
        [SerializeField, Min(0f)] private float focusHeight = 1.55f;
        [SerializeField, Range(-75f, 75f)] private float initialPitch = 15f;
        [SerializeField, Range(-75f, -5f)] private float minimumPitch = -55f;
        [SerializeField, Range(5f, 80f)] private float maximumPitch = 65f;
        [SerializeField, Range(0.1f, 10f)] private float mouseSensitivity = 2.5f;
        [SerializeField, Range(1f, 40f)] private float followResponsiveness = 15f;
        [SerializeField, Min(0f)] private float collisionRadius = 0.18f;
        [SerializeField] private LayerMask collisionLayers = ~0;
        [Header("Target Lock Camera Framing")]
        [Tooltip("Raise the camera above the player's head during target lock.")]
        [SerializeField, Min(0f)] private float lockHeightAboveHead = 1.1f;
        [Tooltip("Shift lock-on camera to the player's right to keep the target visible past the player.")]
        [SerializeField, Range(-3f, 3f)] private float lockShoulderOffset = 1.35f;

        private readonly RaycastHit[] hits = new RaycastHit[20];
        private Transform target;
        private Transform lockedTarget;
        private Animator cachedPlayerAnimator;
        private Transform cachedHeadBone;
        private float yaw;
        private float pitch;
        private bool looking;
        public bool IsPointerCaptured => looking && Cursor.lockState == CursorLockMode.Locked;

        public float Yaw => yaw;
        public Transform Target => target;

        // Called by the locally input-authoritative rider, never replicated.
        public void SetLockTarget(Transform opponent)
        {
            lockedTarget = opponent != target ? opponent : null;
        }

        public void Bind(Transform rider)
        {
            if (rider == null || target == rider)
                return;

            target = rider;
            lockedTarget = null;
            cachedPlayerAnimator = null;
            cachedHeadBone = null;
            yaw = rider.eulerAngles.y;
            pitch = initialPitch;
            SnapToTarget();
            CaptureCursor();
        }

        public void Release(Transform rider)
        {
            if (target != rider)
                return;

            lockedTarget = null;
            cachedPlayerAnimator = null;
            cachedHeadBone = null;
            target = null;
            RestoreCursor();
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                RestoreCursor();
                return;
            }

            // Mouse orbit without right-click while captured; Esc opens UI pointer.
            if (Input.GetKeyDown(KeyCode.Escape) || !Application.isFocused)
                RestoreCursor();
            else if (!looking && Input.GetMouseButtonDown(0) &&
                     (UnityEngine.EventSystems.EventSystem.current == null ||
                      !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
                CaptureCursor();

            Vector3 playerHead = GetPlayerHeadPoint();
            Vector3 pivot = playerHead;
            Vector3 desiredPosition;
            Quaternion desiredRotation;
            float desiredDistance = distance;
            if (lockedTarget != null)
            {
                Vector3 targetCenter = GetLockedTargetCenter(lockedTarget);
                Vector3 planar = targetCenter - playerHead;
                planar.y = 0f;
                if (planar.sqrMagnitude < 0.0001f)
                    planar = target.forward;
                planar.Normalize();
                yaw = Mathf.Atan2(planar.x, planar.z) * Mathf.Rad2Deg;

                // Position BEHIND and ABOVE player's head with a shoulder offset:
                // the player's body must not sit on the line to the opponent.
                Vector3 right = Vector3.Cross(Vector3.up, planar);
                Vector3 origin = playerHead + Vector3.up * lockHeightAboveHead;
                Vector3 offset = -planar * distance + right * lockShoulderOffset;
                desiredPosition = origin + offset;
                Vector3 castDirection = offset.normalized;
                float castDistance = offset.magnitude;
                if (collisionRadius > 0f && castDistance > 0.001f)
                {
                    int count = Physics.SphereCastNonAlloc(origin, collisionRadius,
                        castDirection, hits, castDistance, collisionLayers,
                        QueryTriggerInteraction.Ignore);
                    float allowed = castDistance;
                    for (int i = 0; i < count; i++)
                    {
                        if (hits[i].collider == null ||
                            hits[i].collider.transform.IsChildOf(target))
                            continue;
                        allowed = Mathf.Min(allowed,
                            Mathf.Max(0.2f, hits[i].distance - 0.05f));
                    }
                    desiredPosition = origin + castDirection * allowed;
                }
                // Look direction is calculated FROM THE CAMERA, not from the
                // head pivot, otherwise the target may be off-screen.
                float t = 1f - Mathf.Exp(-followResponsiveness * Time.unscaledDeltaTime);
                transform.position = Vector3.Lerp(transform.position, desiredPosition, t);
                Vector3 view = targetCenter - transform.position;
                if (view.sqrMagnitude > 0.0001f)
                {
                    desiredRotation = Quaternion.LookRotation(view, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        desiredRotation, t);
                    pitch = Mathf.Clamp(transform.eulerAngles.x > 180f
                        ? transform.eulerAngles.x - 360f : transform.eulerAngles.x,
                        minimumPitch, maximumPitch);
                }
                return;
            }

            // Unlocked camera retains existing mouse orbit and collision logic.
            if (looking && Application.isFocused)
            {
                yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
                pitch = Mathf.Clamp(
                    pitch - Input.GetAxis("Mouse Y") * mouseSensitivity,
                    minimumPitch, maximumPitch);
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 backward = -(rotation * Vector3.forward);

            if (collisionRadius > 0f)
            {
                int count = Physics.SphereCastNonAlloc(
                    pivot, collisionRadius, backward, hits,
                    distance, collisionLayers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    if (hits[i].collider == null ||
                        hits[i].collider.transform.IsChildOf(target))
                        continue;

                    desiredDistance = Mathf.Min(
                        desiredDistance, Mathf.Max(0.2f, hits[i].distance - 0.05f));
                }
            }

            desiredPosition = pivot + backward * desiredDistance;
            float smoothing = 1f - Mathf.Exp(-followResponsiveness * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothing);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, smoothing);
        }

        private Vector3 GetPlayerHeadPoint()
        {
            if (target == null) return transform.position;

            // Imported Forms can replace their Animator at runtime.
            Animator animator = target.GetComponentInChildren<Animator>(true);
            if (animator != cachedPlayerAnimator)
            {
                cachedPlayerAnimator = animator;
                cachedHeadBone = animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            }
            if (cachedHeadBone != null && cachedHeadBone.gameObject.activeInHierarchy)
                return cachedHeadBone.position;

            CharacterController controller = target.GetComponent<CharacterController>();
            if (controller != null && controller.enabled)
                return new Vector3(controller.bounds.center.x,
                    controller.bounds.max.y - controller.bounds.size.y * 0.08f,
                    controller.bounds.center.z);
            CapsuleCollider capsule = target.GetComponent<CapsuleCollider>();
            if (capsule != null && capsule.enabled)
                return new Vector3(capsule.bounds.center.x,
                    capsule.bounds.max.y - capsule.bounds.size.y * 0.08f,
                    capsule.bounds.center.z);
            return target.position + Vector3.up * focusHeight;
        }

        private Vector3 GetLockedTargetCenter(Transform opponent)
        {
            // Use the body collision bounds, rather than the fixed camera focus height.
            // CharacterController is preferred when present; SimpleKCC-based rider
            // prefabs can use a capsule collider instead.
            CharacterController controller = opponent.GetComponent<CharacterController>();
            if (controller != null && controller.enabled)
                return controller.bounds.center;

            CapsuleCollider capsule = opponent.GetComponent<CapsuleCollider>();
            if (capsule != null && capsule.enabled)
                return capsule.bounds.center;

            // Fallback for a rider that has no physical body collider.
            return opponent.position + Vector3.up * focusHeight;
        }

        private void SnapToTarget()
        {
            if (target == null)
                return;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot = GetPlayerHeadPoint();
            transform.SetPositionAndRotation(
                pivot - (rotation * Vector3.forward) * distance,
                rotation);
        }

        private void CaptureCursor()
        {
            looking = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void RestoreCursor()
        {
            if (!looking)
                return;

            looking = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDisable()
        {
            RestoreCursor();
        }
    }
}
