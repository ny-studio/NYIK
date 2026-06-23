using NUnit.Framework;
using UnityEngine;
using NYIK.Humanoid;

namespace NYIK.Tests
{
    /// <summary>
    /// ハンド回転キャリブの特性化テスト(VRChat/VRIK 合わせ込み)。
    /// 「コントローラ向き ↔ 手ボーンのバインド向き」のオフセット計算は決定論的な四元数演算なので、
    /// 合成入力で出力を assert すれば VR/Play 無しで正しさ(キャリブ瞬間に手がバインドへ・以後 1:1 追従)
    /// を検証できる。手の捻れ修正(frame-1 自動キャリブ → 明示キャリブ)の回帰ガード。
    /// </summary>
    public class ArmSolverHandCalibrationTests
    {
        [Test]
        public void AtCalibrationInstant_HandSnapsToBind_RegardlessOfControllerOrientation()
        {
            // 任意のコントローラ向き(機種ごとにバラバラ)でも、キャリブ瞬間は手がバインドへ。
            var rCal = Quaternion.Euler(12f, 47f, -80f);
            var bind = Quaternion.Euler(0f, 90f, 0f);

            var offset = ArmSolver.ComputeHandRotationOffset(rCal, bind);
            var applied = ArmSolver.ApplyHandRotationOffset(rCal, offset);

            AssertSameRotation(bind, applied,
                "キャリブ瞬間はコントローラ向きに依らず手はバインドへ収束すべき。");
        }

        [Test]
        public void ControllerWorldDelta_RotatesHandByExactSameDelta()
        {
            // コントローラを world で dR だけ回したら、手も world で dR だけ回る(1:1 追従)。
            var rCal = Quaternion.Euler(5f, 20f, 10f);
            var bind = Quaternion.Euler(0f, 90f, 0f);
            var offset = ArmSolver.ComputeHandRotationOffset(rCal, bind);

            var dR = Quaternion.Euler(0f, 35f, 0f);                 // 35°ヨー
            var applied = ArmSolver.ApplyHandRotationOffset(dR * rCal, offset);

            AssertSameRotation(dR * bind, applied,
                "コントローラの world delta と同じ delta だけ手が回るべき(1:1 追従)。");
        }

        [Test]
        public void MultiAxisDelta_TracksOneToOne()
        {
            var rCal = Quaternion.Euler(-30f, 110f, 15f);
            var bind = Quaternion.Euler(10f, 0f, -25f);
            var offset = ArmSolver.ComputeHandRotationOffset(rCal, bind);

            var dR = Quaternion.Euler(18f, -42f, 60f);              // 多軸 delta
            var applied = ArmSolver.ApplyHandRotationOffset(dR * rCal, offset);

            AssertSameRotation(dR * bind, applied,
                "多軸 world delta でも手はバインドから同じ delta だけ回るべき。");
        }

        [Test]
        public void Identity_GivesIdentity()
        {
            var offset = ArmSolver.ComputeHandRotationOffset(Quaternion.identity, Quaternion.identity);
            var applied = ArmSolver.ApplyHandRotationOffset(Quaternion.identity, offset);
            AssertSameRotation(Quaternion.identity, applied, "恒等→恒等。");
        }

        // q と -q は同一回転を表すため dot の絶対値で比較する。
        static void AssertSameRotation(Quaternion expected, Quaternion actual, string msg)
        {
            float d = Mathf.Abs(Quaternion.Dot(expected, actual));
            Assert.GreaterOrEqual(d, 1f - 1e-4f, $"{msg} (|dot|={d:F6})");
        }
    }
}
