using NUnit.Framework;
using UnityEngine;
using NYIK.Core;
using NYIK.Solvers;

namespace NYIK.Tests
{
    /// <summary>
    /// TwoBoneIKSolver.OnSolve の通し統合テスト（純関数でなく実際に Transform 階層を動かす層）。
    /// 腕/脚IKの中核で、tip 到達・骨長保存・Weight=0不変・退化時 NaN 無しを固定する。
    /// 純関数テストでは捕まらない「逐次回転適用 + tip twist 復元 + Weight slerp」の orchestration を守る。
    /// </summary>
    public class TwoBoneIKSolverIntegrationTests
    {
        GameObject _rig;
        const float UpperLen = 0.5f;
        const float LowerLen = 0.5f;

        // root(0,0,0) → mid(0.5,0,0) → tip(1,0,0) の2骨チェーンを組んで初期化済み solver を返す。
        TwoBoneIKSolver MakeSolver(out Transform tip)
        {
            _rig = new GameObject("root");
            var mid = new GameObject("mid").transform;
            tip = new GameObject("tip").transform;
            mid.SetParent(_rig.transform, false);
            tip.SetParent(mid, false);
            _rig.transform.position = Vector3.zero;
            mid.localPosition = new Vector3(UpperLen, 0f, 0f);
            tip.localPosition = new Vector3(LowerLen, 0f, 0f);

            var solver = new TwoBoneIKSolver();
            solver.SetBones(new BoneTransform(_rig.transform), new BoneTransform(mid), new BoneTransform(tip));
            solver.Initialize(_rig.transform);
            return solver;
        }

        [TearDown]
        public void TearDown()
        {
            if (_rig != null) Object.DestroyImmediate(_rig);
            _rig = null;
        }

        static bool HasNaN(Vector3 v) => float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z);

        [Test]
        public void Solve_Initializes_BoneLengthsCached()
        {
            var solver = MakeSolver(out _);
            Assert.IsTrue(solver.IsInitialized, "有効な3骨で Initialize 成功");
        }

        [Test]
        public void Solve_ReachableTarget_TipReachesTarget()
        {
            var solver = MakeSolver(out var tip);
            var target = new Vector3(0.6f, 0.4f, 0f); // |root→target|≈0.72 ∈ (0, 1.0) ＝到達可能
            solver.TargetPosition = target;
            solver.Solve();

            Assert.IsFalse(HasNaN(tip.position), "NaN を出さない");
            Assert.Less((tip.position - target).magnitude, 0.02f, "tip が target に届く");
        }

        [Test]
        public void Solve_PreservesBoneLengths()
        {
            var solver = MakeSolver(out var tip);
            solver.TargetPosition = new Vector3(0.6f, 0.4f, 0f);
            solver.Solve();

            Vector3 root = _rig.transform.position;
            Vector3 mid = tip.parent.position;
            Assert.AreEqual(UpperLen, (mid - root).magnitude, 0.01f, "上骨長を保存");
            Assert.AreEqual(LowerLen, (tip.position - mid).magnitude, 0.01f, "下骨長を保存");
        }

        [Test]
        public void Solve_WeightZero_LeavesTipUnchanged()
        {
            var solver = MakeSolver(out var tip);
            Vector3 before = tip.position; // (1,0,0)
            solver.Weight = 0f;
            solver.TargetPosition = new Vector3(0.6f, 0.4f, 0f);
            solver.Solve();
            Assert.Less((tip.position - before).magnitude, 1e-4f, "Weight=0 は不変(Solve 早期 return)");
        }

        [Test]
        public void Solve_UnreachableTarget_NoNaN_FullyExtends()
        {
            var solver = MakeSolver(out var tip);
            solver.TargetPosition = new Vector3(2f, 0f, 0f); // reach 1.0 を超える
            solver.Solve();

            Assert.IsFalse(HasNaN(tip.position), "退化(過伸展)でも NaN 無し");
            float reach = (tip.position - _rig.transform.position).magnitude;
            Assert.Greater(reach, 0.9f, "target 方向へ完全伸展(≈最大リーチ)");
            Assert.Less(reach, 1.05f);
        }
    }
}
