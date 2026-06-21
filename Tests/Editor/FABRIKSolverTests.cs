using NUnit.Framework;
using UnityEngine;
using NYIK.Solvers;

namespace NYIK.Tests
{
    /// <summary>
    /// FABRIK（反復IK・spine/tail 用）のリーチ純関数
    /// <see cref="FABRIKSolver.ForwardReach"/> / <see cref="FABRIKSolver.BackwardReach"/> の
    /// 回帰ガード特性化テスト。チェーンが剛（各ボーン長保存）に保たれ、端点がピン留めされ、
    /// 到達可能 target へ収束することを固定する。
    /// </summary>
    public class FABRIKSolverTests
    {
        const float Tol = 1e-4f;

        // 4点・3セグメント（x軸上）。boneLengths[i] = |pos[i]→pos[i+1]|。末尾[3]はリーチ未使用。
        static Vector3[] MakeChain() => new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(2f, 0f, 0f),
            new Vector3(3f, 0f, 0f),
        };
        static float[] Lengths() => new[] { 1f, 1f, 1f, 1f };

        static void AssertSegmentLengths(Vector3[] pos, float[] len, float tol)
        {
            for (int i = 0; i < pos.Length - 1; i++)
                Assert.AreEqual(len[i], (pos[i + 1] - pos[i]).magnitude, tol,
                    $"セグメント{i}の長さが保存される。");
        }

        [Test]
        public void BackwardReach_PinsRoot_PreservesLengths()
        {
            var pos = MakeChain();
            var len = Lengths();
            var root = new Vector3(0.2f, 0.5f, -0.1f);
            FABRIKSolver.BackwardReach(pos, len, root);
            Assert.AreEqual(root, pos[0], "pos[0] が root に固定される。");
            AssertSegmentLengths(pos, len, Tol);
        }

        [Test]
        public void ForwardReach_PinsTarget_PreservesLengths()
        {
            var pos = MakeChain();
            var len = Lengths();
            var target = new Vector3(2.5f, 0.5f, 0.3f);
            FABRIKSolver.ForwardReach(pos, len, target);
            Assert.AreEqual(target, pos[pos.Length - 1], "pos[last] が target に固定される。");
            AssertSegmentLengths(pos, len, Tol);
        }

        [Test]
        public void ForwardThenBackward_ConvergesToReachableTarget()
        {
            var pos = MakeChain();
            var len = Lengths();
            var root = new Vector3(0f, 0f, 0f);
            var target = new Vector3(1.5f, 1.0f, 0f); // root から ~1.8 < 全長3 ＝ 到達可能

            for (int iter = 0; iter < 30; iter++)
            {
                FABRIKSolver.ForwardReach(pos, len, target);
                FABRIKSolver.BackwardReach(pos, len, root);
            }

            Assert.AreEqual(root, pos[0], "root はピン留めのまま。");
            Assert.Less((pos[pos.Length - 1] - target).magnitude, 1e-3f, "end effector が target へ収束。");
            AssertSegmentLengths(pos, len, 1e-3f);
        }

        [Test]
        public void Degenerate_CoincidentPoints_NoNaN_PreservesLength()
        {
            // 全点が一致 → diff=0 で Vector3.up フォールバック。NaN を出さず長さは保たれる。
            var pos = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
            var len = new[] { 1f, 1f, 1f };
            FABRIKSolver.BackwardReach(pos, len, Vector3.zero);
            foreach (var p in pos)
                Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z), "NaN 無し。");
            Assert.AreEqual(1f, (pos[1] - pos[0]).magnitude, Tol, "退化でも長さ保存。");
        }
    }
}
