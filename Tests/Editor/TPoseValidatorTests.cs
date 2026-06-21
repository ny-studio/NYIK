using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using NYIK.Calibration;
using NYIK.Tracker;

namespace NYIK.Tests
{
    public class TPoseValidatorTests
    {
        readonly List<GameObject> _created = new();

        [TearDown]
        public void Teardown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        [Test]
        public void Validate_ProperTPose_Passes()
        {
            var provider = BuildProvider(
                headPos: new Vector3(0, 1.6f, 0),
                headRot: Quaternion.identity,
                leftHandPos: new Vector3(-0.7f, 1.4f, 0),
                rightHandPos: new Vector3(0.7f, 1.4f, 0));

            var result = TPoseValidator.Validate(provider);
            Assert.IsTrue(result.IsTPose, "Symmetric T-pose should pass: " + result.Summary);
        }

        [Test]
        public void Validate_HandsAtHip_Fails()
        {
            var provider = BuildProvider(
                headPos: new Vector3(0, 1.6f, 0),
                headRot: Quaternion.identity,
                leftHandPos: new Vector3(-0.1f, 1.0f, 0),
                rightHandPos: new Vector3(0.1f, 1.0f, 0));

            var result = TPoseValidator.Validate(provider);
            Assert.IsFalse(result.IsTPose, "Hands at hip should fail validation.");
        }

        [Test]
        public void Validate_HeadTilted_Fails()
        {
            var provider = BuildProvider(
                headPos: new Vector3(0, 1.6f, 0),
                headRot: Quaternion.Euler(0, 0, 60f), // head rolled
                leftHandPos: new Vector3(-0.7f, 1.4f, 0),
                rightHandPos: new Vector3(0.7f, 1.4f, 0));

            var result = TPoseValidator.Validate(provider);
            Assert.IsFalse(result.IsTPose, "Tilted head should fail validation.");
        }

        [Test]
        public void Validate_HandTooFarForward_Fails()
        {
            var provider = BuildProvider(
                headPos: new Vector3(0, 1.6f, 0),
                headRot: Quaternion.identity,
                leftHandPos: new Vector3(-0.7f, 1.4f, 0.5f),
                rightHandPos: new Vector3(0.7f, 1.4f, 0.5f));

            var result = TPoseValidator.Validate(provider);
            Assert.IsFalse(result.IsTPose, "Hands drifted forward should fail validation.");
        }

        // Build a minimal provider with Head + L/R hand slots at the requested poses.
        SimpleProvider BuildProvider(Vector3 headPos, Quaternion headRot,
                                     Vector3 leftHandPos, Vector3 rightHandPos)
        {
            var head = MakeTransform("head", headPos, headRot);
            var lh = MakeTransform("left", leftHandPos, Quaternion.identity);
            var rh = MakeTransform("right", rightHandPos, Quaternion.identity);
            var p = new SimpleProvider();
            p.Add(TrackerSlotKind.Head, head);
            p.Add(TrackerSlotKind.LeftHand, lh);
            p.Add(TrackerSlotKind.RightHand, rh);
            return p;
        }

        Transform MakeTransform(string name, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.transform.SetPositionAndRotation(pos, rot);
            return go.transform;
        }

        sealed class SimpleProvider : ITrackerSourceProvider
        {
            readonly List<TrackerSlot> _slots = new();
            readonly Dictionary<TrackerSlotKind, TrackerSlot> _byKind = new();
            public IReadOnlyList<TrackerSlot> Slots => _slots;
            public bool HasFullBodyTrackers => false;
            public TrackerSlot GetSlot(TrackerSlotKind kind) =>
                _byKind.TryGetValue(kind, out var s) ? s : null;
            public void Tick(float dt) { }

            public void Add(TrackerSlotKind kind, Transform t)
            {
                var slot = new TrackerSlot(kind) { Source = t, IsTracking = true };
                _slots.Add(slot);
                _byKind[kind] = slot;
            }
        }
    }
}
