using System.Collections.Generic;
using MuOnBaAr.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace MuOnBaAr.Tests
{
    public sealed class RiderLineOfSightRulesTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly Vector3 origin = new Vector3(7100f, 520f, 8100f);
        private GameObject attacker;
        private GameObject target;

        private GameObject Make(string name, Vector3 position, Transform parent = null)
        {
            GameObject go = new GameObject(name);
            objects.Add(go);
            if (parent != null)
                go.transform.SetParent(parent);
            go.transform.position = position;
            return go;
        }

        private BoxCollider MakeBox(string name, Vector3 position, Transform parent = null)
        {
            GameObject go = Make(name, position, parent);
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = Vector3.one * 0.35f;
            return box;
        }

        [SetUp]
        public void SetUp()
        {
            attacker = Make("LOS Attacker", origin);
            target = Make("LOS Target", origin + Vector3.forward * 2f);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null)
                    Object.DestroyImmediate(objects[i]);
            objects.Clear();
            Physics.SyncTransforms();
        }

        private bool Visible(int mask = ~0)
        {
            Physics.SyncTransforms();
            return RiderLineOfSightRules.HasClearPath(
                origin, target.transform.position, attacker.transform,
                target.transform, mask);
        }

        [Test]
        public void ClearAirWithNoCollidersIsVisible()
        {
            Assert.That(Visible(), Is.True);
        }

        [Test]
        public void OwnForwardMarkerColliderIsIgnored()
        {
            MakeBox("Cube", origin + Vector3.forward * 0.3f, attacker.transform);
            MakeBox("Target Body", target.transform.position, target.transform);
            Assert.That(Visible(), Is.True);
        }

        [Test]
        public void SolidWallBetweenRidersBlocksLineOfSight()
        {
            MakeBox("Solid Wall", origin + Vector3.forward);
            Assert.That(Visible(), Is.False);
        }

        [Test]
        public void TargetChildColliderCountsAsVisibleTarget()
        {
            MakeBox("Target Body", target.transform.position, target.transform);
            Assert.That(Visible(), Is.True);
        }

        [Test]
        public void AWallBehindTargetDoesNotBlockTarget()
        {
            // Test the ray ending before a wall beyond the target.
            MakeBox("Target Body", target.transform.position, target.transform);
            MakeBox("Wall Behind", origin + Vector3.forward * 3f);
            Assert.That(Visible(), Is.True);
        }

        [Test]
        public void TriggerCollidersDoNotBlock()
        {
            BoxCollider trigger = MakeBox("Trigger", origin + Vector3.forward);
            trigger.isTrigger = true;
            Assert.That(Visible(), Is.True);
        }

        [Test]
        public void InspectorLayerMaskCanExcludeAnObstacle()
        {
            BoxCollider box = MakeBox("Excluded Wall", origin + Vector3.forward);
            box.gameObject.layer = 8;
            Assert.That(Visible(~0), Is.False);
            Assert.That(Visible(~(1 << 8)), Is.True);
        }

        [Test]
        public void WallStillBlocksAfterOwnMarkerIsIgnored()
        {
            MakeBox("Cube", origin + Vector3.forward * 0.25f, attacker.transform);
            MakeBox("Wall", origin + Vector3.forward);
            MakeBox("Target Body", target.transform.position, target.transform);
            Assert.That(Visible(), Is.False);
        }

        [Test]
        public void AnotherRidersColliderCountsAsObstacle()
        {
            GameObject bystander = Make("Other Rider", origin + Vector3.forward);
            MakeBox("Body", bystander.transform.position, bystander.transform);
            Assert.That(Visible(), Is.False);
        }

        [Test]
        public void MissingRootsFailClosed()
        {
            Assert.That(RiderLineOfSightRules.HasClearPath(
                origin, target.transform.position, null,
                target.transform, ~0), Is.False);
        }
    }
}
