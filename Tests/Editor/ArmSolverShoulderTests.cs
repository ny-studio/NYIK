using NUnit.Framework;
using UnityEngine;
using NYIK.Humanoid;

namespace NYIK.Tests
{
    /// <summary>
    /// 肩アクティベーションの特性化テスト（VRChat/VRIK 合わせ込み）。
    /// IK は決定論的な数学なので、合成入力を入れて出力を assert すれば VR/Play 無しで
    /// 「正しさ」を検証できる（体感は別途ヘッドセットで）。これが合わせ込みを自律ループ
    /// 可能にする検証パターンの雛形。
    /// </summary>
    public class ArmSolverShoulderTests
    {
        [Test]
        public void ShoulderActivation_IsContinuousAtMidReach_NotDeadZoned()
        {
            // VRChat/VRIK 整合の要: 中間 reach(肘を曲げて寄る姿勢)で肩が効くこと。
            // 旧デッドゾーン式は 50% reach で 0 を返し、肩が死んでいた。
            float a = ArmSolver.ShoulderActivation(0.5f, 0.2f);
            Assert.Greater(a, 0f, "肩は中間 reach で連続的に効くべき(旧デッドゾーンでは 0 だった)。");
            Assert.AreEqual(Mathf.InverseLerp(0.2f, 1f, 0.5f), a, 1e-5f);
        }

        [Test]
        public void ShoulderActivation_ZeroBelowStart_FullAtMaxReach()
        {
            Assert.AreEqual(0f, ArmSolver.ShoulderActivation(0.1f, 0.2f), 1e-5f, "下限未満は 0。");
            Assert.AreEqual(1f, ArmSolver.ShoulderActivation(1.0f, 0.2f), 1e-5f, "full reach で 1。");
            Assert.AreEqual(1f, ArmSolver.ShoulderActivation(1.5f, 0.2f), 1e-5f, "腕長超でも 1 にクランプ。");
        }

        [Test]
        public void ShoulderActivation_MonotonicNonDecreasing()
        {
            float prev = -1f;
            for (float r = 0f; r <= 1.2f; r += 0.1f)
            {
                float a = ArmSolver.ShoulderActivation(r, 0.2f);
                Assert.GreaterOrEqual(a, prev, $"reachRatio={r:F1} で単調非減少であるべき。");
                prev = a;
            }
        }

        [Test]
        public void ShoulderActivation_SerializedSceneValue_NowEngagesEarly()
        {
            // 録画シーンのシリアライズ値 reachStart=0.1。旧式ではこれが「上端デッドゾーン幅 0.1」
            // ＝ ~90% reach まで肩が死んでいた。新式では「下限 0.1 から連続」＝録画シーンを
            // 触らずに修正が効くことを保証する回帰ガード。
            float midReach = ArmSolver.ShoulderActivation(0.5f, 0.1f);
            Assert.Greater(midReach, 0f,
                "serialized 0.1 でも 50% reach で肩が効くこと(シーン非接触の修正の回帰ガード)。");
        }
    }
}
