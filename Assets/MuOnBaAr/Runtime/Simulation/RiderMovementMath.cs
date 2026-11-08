using UnityEngine;

namespace MuOnBaAr.Runtime
{
    /// <summary>Convert camera-relative 2D input to an authoritative planar world direction.</summary>
    public static class RiderMovementMath
    {
        public static Vector2 CameraRelativeToWorld(Vector2 move, float cameraYawDegrees)
        {
            move = Vector2.ClampMagnitude(move, 1f);
            float radians = cameraYawDegrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                move.x * cosine + move.y * sine,
                move.y * cosine - move.x * sine);
        }
    }
}
