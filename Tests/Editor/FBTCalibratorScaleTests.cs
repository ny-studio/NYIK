using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using NYIK.Calibration;
using NYIK.Estimator;

namespace NYIK.Tests
{
    /// <summary>
    /// ユーザー身長/腕スパン スケール計測の特性化テスト（VRChat 合わせ込み 優先3・純関数部）。
    /// ランタイム適用（二重スケール注意）は別途慎重実装。ここは「計測値→倍率」の算出のみ検証。
    /// </summary>
    public class FBTCalibratorScaleTests
    {
        [Test]
        public void ComputeUserScale_None_AlwaysOne()
        {
            Assert.AreEqual(1f, FBTCalibrator.ComputeUserScale(UserScaleMode.None, 1.7f, 1.6f), 1e-6f);
            Assert.AreEqual(1f, FBTCalibrator.ComputeUserScale(UserScaleMode.None, 99f, 1f), 1e-6f);
        }

        [Test]
        public void ComputeUserScale_Height_RatioOfUserToAvatar()
        {
            float s = FBTCalibrator.ComputeUserScale(UserScaleMode.Height, 1.7f, 1.6f);
            Assert.AreEqual(1.7f / 1.6f, s, 1e-5f, "身長比 = user/avatar。");
        }

        [Test]
        public void ComputeUserScale_ArmSpan_RatioOfUserToAvatar()
        {
            float s = FBTCalibrator.ComputeUserScale(UserScaleMode.ArmSpan, 1.6f, 1.5f);
            Assert.AreEqual(1.6f / 1.5f, s, 1e-5f, "腕スパン比 = user/avatar。");
        }

        [Test]
        public void ComputeUserScale_ClampsToRange()
        {
            Assert.AreEqual(3f, FBTCalibrator.ComputeUserScale(UserScaleMode.Height, 5f, 1f), 1e-5f, "上限クランプ。");
            Assert.AreEqual(0.3f, FBTCalibrator.ComputeUserScale(UserScaleMode.Height, 0.1f, 1f), 1e-5f, "下限クランプ。");
        }

        [Test]
        public void ComputeUserScale_InvalidInputs_FallBackToOne()
        {
            Assert.AreEqual(1f, FBTCalibrator.ComputeUserScale(UserScaleMode.Height, 1.7f, 0f), 1e-6f, "avatar=0 は安全側 1。");
            Assert.AreEqual(1f, FBTCalibrator.ComputeUserScale(UserScaleMode.Height, 0f, 1.6f), 1e-6f, "user=0 は安全側 1。");
            Assert.AreEqual(1f, FBTCalibrator.ComputeUserScale(UserScaleMode.ArmSpan, -1f, 1.5f), 1e-6f, "負値も安全側 1。");
        }

        // ── ComputeTargetRemapScale（逆数・哲学B：ターゲットを縮める）──

        [Test]
        public void ComputeTargetRemapScale_None_IsOne()
        {
            Assert.AreEqual(1f, FBTCalibrator.ComputeTargetRemapScale(UserScaleMode.None, 1.7f, 1.6f), 1e-6f);
        }

        [Test]
        public void ComputeTargetRemapScale_IsReciprocalOfComputeUserScale()
        {
            // 反二重スケール不変条件: remap * fit ≈ 1（user空間→avatar空間で打ち消し合う）。
            float fit = FBTCalibrator.ComputeUserScale(UserScaleMode.Height, 1.7f, 1.6f);
            float remap = FBTCalibrator.ComputeTargetRemapScale(UserScaleMode.Height, 1.7f, 1.6f);
            Assert.AreEqual(1.6f / 1.7f, remap, 1e-5f, "remap = avatar/user。");
            Assert.AreEqual(1f, remap * fit, 1e-5f, "remap * fit = 1（二重スケール打消し）。");
        }

        [Test]
        public void ComputeTargetRemapScale_EqualMeasures_IsExactlyOne()
        {
            Assert.AreEqual(1f, FBTCalibrator.ComputeTargetRemapScale(UserScaleMode.Height, 1.6f, 1.6f), 1e-6f);
        }

        [Test]
        public void ComputeTargetRemapScale_Clamps()
        {
            float r = FBTCalibrator.ComputeTargetRemapScale(UserScaleMode.Height, 0.5f, 5f);
            Assert.LessOrEqual(r, 3f);
            Assert.GreaterOrEqual(r, 0.3f);
        }

        [Test]
        public void ComputeTargetRemapScale_TinyNonzeroMeasure_FallsBackToOne()
        {
            // 退化窓 (1e-4, 1e-3) が暴走スケールにならず 1.0（critic 指摘の穴）。
            Assert.AreEqual(1f, FBTCalibrator.ComputeTargetRemapScale(UserScaleMode.Height, 1e-4f, 1e-3f), 1e-6f);
            Assert.AreEqual(1f, FBTCalibrator.ComputeTargetRemapScale(UserScaleMode.Height, 0.04f, 1.6f), 1e-6f);
        }

        // ── ScaleAboutPivot（pivot 不動点の相似変換）──

        [Test]
        public void ScaleAboutPivot_Identity_ReturnsInput()
        {
            var p = new Vector3(1, 2, 3);
            Assert.AreEqual(p, FBTCalibrator.ScaleAboutPivot(p, new Vector3(0, 1, 0), 1f));
        }

        [Test]
        public void ScaleAboutPivot_PivotIsFixedPoint()
        {
            var pivot = new Vector3(0.1f, 0f, -0.2f);
            Assert.AreEqual(pivot, FBTCalibrator.ScaleAboutPivot(pivot, pivot, 0.7f));
        }

        [Test]
        public void ScaleAboutPivot_FloorPivot_KeepsPivotHeight()
        {
            // 床(pivot.y)にある足は床に留まる（沈み/浮き回避）。
            var pivot = new Vector3(0, 0f, 0);
            var foot = new Vector3(0.15f, 0f, 0.1f);
            Assert.AreEqual(0f, FBTCalibrator.ScaleAboutPivot(foot, pivot, 0.9f).y, 1e-6f);
        }

        [Test]
        public void ScaleAboutPivot_ScalesDisplacementByS()
        {
            var pivot = new Vector3(0, 1, 0);
            var p = new Vector3(0, 2, 0); // 変位 (0,1,0)
            Assert.AreEqual(new Vector3(0, 1.5f, 0), FBTCalibrator.ScaleAboutPivot(p, pivot, 0.5f));
        }

        // ── ScaleBodyTargetsAboutPivot（estimator 後 body ターゲット一括スケール）──

        static Dictionary<HumanBodyBones, BoneTarget> MakeTargets(
            params (HumanBodyBones bone, Vector3 pos)[] items)
        {
            var d = new Dictionary<HumanBodyBones, BoneTarget>();
            foreach (var it in items)
                d[it.bone] = BoneTarget.Tracked(it.pos, Quaternion.identity);
            return d;
        }

        [Test]
        public void ScaleBodyTargets_ScaleOne_IsNoOp()
        {
            var d = MakeTargets((HumanBodyBones.Hips, new Vector3(0, 1, 0)),
                                (HumanBodyBones.LeftFoot, new Vector3(0, 0, 0)));
            FBTCalibrator.ScaleBodyTargetsAboutPivot(d, new Vector3(0, 0, 0), 1f);
            Assert.AreEqual(new Vector3(0, 1, 0), d[HumanBodyBones.Hips].Position);
            Assert.AreEqual(new Vector3(0, 0, 0), d[HumanBodyBones.LeftFoot].Position);
        }

        [Test]
        public void ScaleBodyTargets_FootHipVector_ScalesByS()
        {
            // 床(0)を pivot に Hips(高さ1)/足(高さ0) を 0.5 倍 → 足-腰 距離が 0.5×。
            var d = MakeTargets((HumanBodyBones.Hips, new Vector3(0, 1, 0)),
                                (HumanBodyBones.LeftFoot, new Vector3(0, 0, 0)));
            FBTCalibrator.ScaleBodyTargetsAboutPivot(d, new Vector3(0, 0, 0), 0.5f);
            Vector3 hip = d[HumanBodyBones.Hips].Position;
            Vector3 foot = d[HumanBodyBones.LeftFoot].Position;
            Assert.AreEqual(0.5f, hip.y, 1e-5f, "腰高さが半分。");
            Assert.AreEqual(0f, foot.y, 1e-5f, "床にある足は床に留まる。");
            Assert.AreEqual(0.5f, (hip - foot).magnitude, 1e-5f, "足-腰ベクトルが s 倍。");
        }

        [Test]
        public void ScaleBodyTargets_ExcludesHeadAndHands()
        {
            // Head / L/R Hand は HMD・コントローラ実測なのでスケールしない。
            var head = new Vector3(0, 5, 0);
            var lh = new Vector3(1, 5, 0);
            var rh = new Vector3(-1, 5, 0);
            var d = MakeTargets((HumanBodyBones.Head, head),
                                (HumanBodyBones.LeftHand, lh),
                                (HumanBodyBones.RightHand, rh),
                                (HumanBodyBones.Spine, new Vector3(0, 2, 0)));
            FBTCalibrator.ScaleBodyTargetsAboutPivot(d, new Vector3(0, 0, 0), 0.5f);
            Assert.AreEqual(head, d[HumanBodyBones.Head].Position, "Head は不変。");
            Assert.AreEqual(lh, d[HumanBodyBones.LeftHand].Position, "LeftHand は不変。");
            Assert.AreEqual(rh, d[HumanBodyBones.RightHand].Position, "RightHand は不変。");
            Assert.AreEqual(new Vector3(0, 1, 0), d[HumanBodyBones.Spine].Position, "Spine はスケールされる。");
        }

        [Test]
        public void ScaleBodyTargets_SkipsPositionlessTargets()
        {
            // HasPosition=false（回転のみ等）の body ターゲットは触らない。
            var d = new Dictionary<HumanBodyBones, BoneTarget>
            {
                [HumanBodyBones.Spine] = BoneTarget.RotationOnly(Quaternion.identity, 1f),
            };
            FBTCalibrator.ScaleBodyTargetsAboutPivot(d, new Vector3(0, 0, 0), 0.5f);
            Assert.IsFalse(d[HumanBodyBones.Spine].HasPosition, "位置なしターゲットは不変。");
        }

        [Test]
        public void ScaleBodyTargets_NullDict_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                FBTCalibrator.ScaleBodyTargetsAboutPivot(null, Vector3.zero, 0.5f));
        }
    }
}
