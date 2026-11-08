using MuOnBaAr.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace MuOnBaAr.Tests
{
    public sealed class RiderMovementMathTests
    {
        [Test]
        public void ForwardWithZeroYawMovesAlongWorldZ()
        {
            Vector2 result = RiderMovementMath.CameraRelativeToWorld(Vector2.up, 0f);
            Assert.That(result.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(result.y, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void ForwardWithRightTurnMovesAlongWorldX()
        {
            Vector2 result = RiderMovementMath.CameraRelativeToWorld(Vector2.up, 90f);
            Assert.That(result.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(result.y, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void DiagonalInputIsClampedBeforeRotation()
        {
            Vector2 result = RiderMovementMath.CameraRelativeToWorld(Vector2.one, 45f);
            Assert.That(result.magnitude, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void ZeroInputRemainsZeroForEveryYaw()
        {
            Vector2 result = RiderMovementMath.CameraRelativeToWorld(Vector2.zero, 217f);
            Assert.That(result, Is.EqualTo(Vector2.zero));
        }
    }
}
