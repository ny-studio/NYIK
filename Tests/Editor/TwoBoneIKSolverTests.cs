using NUnit.Framework;
using UnityEngine;
using NYIK.Solvers;

namespace NYIK.Tests
{
    /// <summary>
    /// 二骨IKの解析コア <see cref="TwoBoneIKSolver.SolveMidPosition"/> の回帰ガード特性化テスト。
    /// 余弦定理で構成した中間関節は、上骨長を厳密に保存し、下骨が target に届くことを固定する
    /// （誰かが式を壊したら即検知）。距離アサートなので root 位置に依存しない。
    /// </summary>
    public class TwoBoneIKSolverTests
    {
        const float Tol = 1e-4f;

        static readonly Vector3 Root = new Vector3(1f, 2f, 3f);
        static readonly Vector3 Dir = Vector3.right;       // 単位・root→target 方向
        static readonly Vector3 Bend = Vector3.up;         // Dir に直交する曲げ方向

        [Test]
        public void PreservesUpperBoneLength()
        {
            // reachable（|u-l|<dist<u+l）な構成で上骨長が厳密保存される。
            Vector3 mid = TwoBoneIKSolver.SolveMidPosition(Root, Dir, 0.8f, 0.5f, 0.5f, Bend);
            Assert.AreEqual(0.5f, (mid - Root).magnitude, Tol, "|mid - root| == upperLen。");
        }

        [Test]
        public void LowerBoneReachesTarget()
        {
            Vector3 target = Root + Dir * 0.8f;
            Vector3 mid = TwoBoneIKSolver.SolveMidPosition(Root, Dir, 0.8f, 0.5f, 0.5f, Bend);
            Assert.AreEqual(0.5f, (mid - target).magnitude, Tol, "|mid - target| == lowerLen（下骨が届く）。");
        }

        [Test]
        public void AsymmetricLengths_BothBonesPreserved()
        {
            Vector3 target = Root + Dir * 0.8f;
            Vector3 mid = TwoBoneIKSolver.SolveMidPosition(Root, Dir, 0.8f, 0.6f, 0.4f, Bend);
            Assert.AreEqual(0.6f, (mid - Root).magnitude, Tol, "上骨長 0.6 保存。");
            Assert.AreEqual(0.4f, (mid - target).magnitude, Tol, "下骨長 0.4 で target 到達。");
        }

        [Test]
        public void FullyExtended_MidLiesOnLine()
        {
            // dist == upper+lower → 完全伸展。perp=0 なので mid は root→target 直線上 root+dir*upper。
            Vector3 mid = TwoBoneIKSolver.SolveMidPosition(Root, Dir, 1.0f, 0.5f, 0.5f, Bend);
            Vector3 expected = Root + Dir * 0.5f;
            Assert.Less((mid - expected).magnitude, Tol, "完全伸展で mid は直線上(root+dir*upper)。");
        }

        [Test]
        public void BentConfig_MidOffsetAlongBendDir()
        {
            // 曲がった構成では中間関節が bendDir 側へ正の変位を持つ（曲げ平面の向きが正しい）。
            Vector3 mid = TwoBoneIKSolver.SolveMidPosition(Root, Dir, 0.8f, 0.5f, 0.5f, Bend);
            Assert.Greater(Vector3.Dot(mid - Root, Bend), 0f, "mid は bendDir 方向へ膨らむ。");
        }

        [Test]
        public void DegenerateTriangle_StaysFinite_NoNaN()
        {
            // dist が到達限界に近くても Clamp により Acos が NaN を出さない（数値安定）。
            Vector3 mid = TwoBoneIKSolver.SolveMidPosition(Root, Dir, 0.999999f, 0.5f, 0.5f, Bend);
            Assert.IsFalse(float.IsNaN(mid.x) || float.IsNaN(mid.y) || float.IsNaN(mid.z), "NaN を出さない。");
        }
    }
}
