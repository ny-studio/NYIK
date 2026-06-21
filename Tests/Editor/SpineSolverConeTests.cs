using NUnit.Framework;
using UnityEngine;
using NYIK.Humanoid;

namespace NYIK.Tests
{
    /// <summary>
    /// Spine の rest 相対コーンクランプの特性化テスト（VRChat 合わせ込み 優先4a）。
    /// 屈み姿勢で背骨が垂直へ戻る実バグの修正を、ヘッドレスで決定論的に検証する。
    /// </summary>
    public class SpineSolverConeTests
    {
        [Test]
        public void ConeClamp_WithinCone_ReturnsUnchanged()
        {
            Vector3 reference = Vector3.up;
            Vector3 dir = Quaternion.AngleAxis(10f, Vector3.right) * Vector3.up; // 10° from up
            Vector3 result = SpineSolver.ConeClamp(dir, reference, 25f);
            Assert.AreEqual(0f, Vector3.Angle(result, dir.normalized), 1e-3f, "コーン内なら変えない。");
        }

        [Test]
        public void ConeClamp_BeyondCone_ClampedToMaxAngleFromReference()
        {
            Vector3 reference = Vector3.up;
            Vector3 dir = Quaternion.AngleAxis(80f, Vector3.right) * Vector3.up; // 80° from up
            Vector3 result = SpineSolver.ConeClamp(dir, reference, 25f);
            Assert.AreEqual(25f, Vector3.Angle(result, reference), 1e-2f, "超過分は maxAngle にクランプ。");
        }

        [Test]
        public void ConeClamp_RestRelative_DoesNotPullHorizontalBackToVertical()
        {
            // 実バグの核: 背骨が水平で親(参照)も水平のとき、垂直へ引き戻さないこと。
            // 旧実装(reference=Vector3.up)なら水平 dir(up から 90°)が up 側へ引かれていた。
            Vector3 reference = Vector3.forward; // 親セグメントが水平
            Vector3 dir = Vector3.forward;       // 自分も水平
            Vector3 result = SpineSolver.ConeClamp(dir, reference, 25f);
            Assert.AreEqual(90f, Vector3.Angle(result, Vector3.up), 1e-2f,
                "親基準コーンは水平の背骨を垂直へ戻さない(rest 相対)。");
        }

        [Test]
        public void ConeClamp_ParentRelativeChain_AccumulatesBendBeyondSingleJointLimit()
        {
            // 親基準を連鎖すると、各関節 maxAngle までしか曲がらなくても累積で大きく倒れる
            // ＝多関節 spine で背中を水平近くまで倒せる(world-up 基準では総量が maxAngle に制限)。
            const float maxAngle = 25f;
            Vector3 reference = Vector3.up;
            float totalFromUp = 0f;
            for (int seg = 0; seg < 3; seg++)
            {
                Vector3 want = Quaternion.AngleAxis(80f, Vector3.right) * reference; // 大きく前傾させたい
                Vector3 clamped = SpineSolver.ConeClamp(want, reference, maxAngle);
                totalFromUp = Vector3.Angle(clamped, Vector3.up);
                reference = clamped; // 次の親 = 今のセグメント
            }
            Assert.Greater(totalFromUp, maxAngle + 1f,
                "親基準の連鎖は単関節制限を超えて累積で倒れる(3×25°≈75°)。");
        }
    }
}
