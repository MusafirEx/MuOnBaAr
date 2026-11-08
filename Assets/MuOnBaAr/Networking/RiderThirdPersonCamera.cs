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

        private readonly RaycastHit[] hits = new RaycastHit[20];
        private Transform target;
        private float yaw;
        private float pitch;
        private bool looking;

        public float Yaw => yaw;
        public Transform Target => target;

        public void Bind(Transform rider)
        {
            if (rider == null || target == rider)
                return;

            target = rider;
            yaw = rider.eulerAngles.y;
            pitch = initialPitch;
            SnapToTarget();
        }

        public void Release(Transform rider)
        {
            if (target != rider)
                return;

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

            if (Input.GetMouseButtonDown(1))
            {
                looking = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (looking && (Input.GetMouseButtonUp(1) || Input.GetKeyDown(KeyCode.Escape)))
                RestoreCursor();

            if (looking && Application.isFocused)
            {
                yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
                pitch = Mathf.Clamp(
                    pitch - Input.GetAxis("Mouse Y") * mouseSensitivity,
                    minimumPitch, maximumPitch);
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot = target.position + Vector3.up * focusHeight;
            Vector3 backward = -(rotation * Vector3.forward);
            float desiredDistance = distance;

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

            Vector3 desiredPosition = pivot + backward * desiredDistance;
            float t = 1f - Mathf.Exp(-followResponsiveness * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, t);
        }

        private void SnapToTarget()
        {
            if (target == null)
                return;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot = target.position + Vector3.up * focusHeight;
            transform.SetPositionAndRotation(
                pivot - (rotation * Vector3.forward) * distance,
                rotation);
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
