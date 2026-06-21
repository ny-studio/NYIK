using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using NYIK.Calibration;
using NYIK.Tracker;

namespace NYIK.Tests
{
    /// <summary>
    /// I-pose 検証の特性化テスト（VRChat 合わせ込み 優先1）。
    /// VRChat 既定の I-pose(腕を体側に下ろす)が通り、T-pose 入力は I-pose 検証では落ちる
    /// （= 両姿勢が別物として正しく判定される）ことをヘッドレスで保証。
    /// </summary>
    public class IPoseValidatorTests
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
        public void ValidateIPose_ProperIPose_Passes()
        {
            // 直立・腕を体側に下ろす: 手は低く(肩より下)・体側に近い・前後ドリフト無し。
            var provider = BuildProvider(
                headPos: new Vector3(0, 1.6f, 0), headRot: Quaternion.identity,
                leftHandPos: new Vector3(-0.18f, 0.78f, 0),
                rightHandPos: new Vector3(0.18f, 0.78f, 0));

            var result = TPoseValidator.ValidateIPose(provider);
            Assert.IsTrue(result.IsTPose, "腕下ろし I-pose は通るべき: " + result.Summary);
        }

        [Test]
        public void ValidateIPose_RejectsTPose_ArmsExtendedAtShoulder()
        {
            // T-pose(腕を肩高で外に伸ばす)は I-pose 検証では落ちる(手が高すぎ＋広すぎ)。
            var provider = BuildProvider(
                headPos: new Vector3(0, 1.6f, 0), headRot: Quaternion.identity,
                leftHandPos: new Vector3(-0.7f, 1.4f, 0),
                rightHandPos: new Vector3(0.7f, 1.4f, 0));

            var result = TPoseValidator.ValidateIPose(provider);
            Assert.IsFalse(result.IsTPose, "T-pose は I-pose 検証では落ちるべき(姿勢が別物)。");
        }

        [Test]
        public void ValidateIPose_RejectsHandsForward()
        {
            var provider = BuildProvider(
                headPos: new Vector3(0, 1.6f, 0), headRot: Quaternion.identity,
                leftHandPos: new Vector3(-0.18f, 0.78f, 0.5f),
                rightHandPos: new Vector3(0.18f, 0.78f, 0.5f));

            var result = TPoseValidator.ValidateIPose(provider);
            Assert.IsFalse(result.IsTPose, "手が前方ドリフトしたら落ちるべき。");
        }

        [Test]
        public void ValidateIPose_RejectsHeadTilted()
        {
            var provider = BuildProvider(
                headPos: new Vector3(0, 1.6f, 0), headRot: Quaternion.Euler(0, 0, 60f),
                leftHandPos: new Vector3(-0.18f, 0.78f, 0),
                rightHandPos: new Vector3(0.18f, 0.78f, 0));

            var result = TPoseValidator.ValidateIPose(provider);
            Assert.IsFalse(result.IsTPose, "頭が傾いていたら落ちるべき。");
        }

        [Test]
        public void IPoseAndTPose_AreDistinct()
        {
            // I-pose データは I-pose 検証を通り T-pose 検証では落ちる。逆も然り。
            var iPose = BuildProvider(
                headPos: new Vector3(0, 1.6f, 0), headRot: Quaternion.identity,
                leftHandPos: new Vector3(-0.18f, 0.78f, 0),
                rightHandPos: new Vector3(0.18f, 0.78f, 0));
            Assert.IsTrue(TPoseValidator.ValidateIPose(iPose).IsTPose);
            Assert.IsFalse(TPoseValidator.Validate(iPose).IsTPose, "I-pose は厳密 T-pose 検証では落ちる。");
        }

        SimpleProvider BuildProvider(Vector3 headPos, Quaternion headRot,
                                     Vector3 leftHandPos, Vector3 rightHandPos)
        {
            var p = new SimpleProvider();
            p.Add(TrackerSlotKind.Head, MakeTransform("head", headPos, headRot));
            p.Add(TrackerSlotKind.LeftHand, MakeTransform("left", leftHandPos, Quaternion.identity));
            p.Add(TrackerSlotKind.RightHand, MakeTransform("right", rightHandPos, Quaternion.identity));
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
