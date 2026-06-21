using System.IO;
using NUnit.Framework;
using UnityEngine;
using NYIK.Calibration;
using NYIK.Tracker;

namespace NYIK.Tests
{
    public class FBTCalibrationDataJsonTests
    {
        string _tmpPath;
        FBTCalibrationData _data;

        [SetUp]
        public void Setup()
        {
            _tmpPath = Path.Combine(Path.GetTempPath(), $"nyik_test_{System.Guid.NewGuid()}.json");
            _data = ScriptableObject.CreateInstance<FBTCalibrationData>();
        }

        [TearDown]
        public void Teardown()
        {
            if (_data != null) Object.DestroyImmediate(_data);
            if (File.Exists(_tmpPath)) File.Delete(_tmpPath);
        }

        [Test]
        public void SaveAndLoad_PreservesEntries()
        {
            // Populate via CaptureFrom with a mock provider.
            var provider = new MockProvider();
            provider.AddSlot(TrackerSlotKind.Waist, Quaternion.Euler(0, 30, 0), new Vector3(0.1f, 0, 0));
            provider.AddSlot(TrackerSlotKind.Chest, Quaternion.Euler(15, 0, 0), new Vector3(0, 0.05f, 0));

            _data.CaptureFrom(provider);

            _data.SaveToJson(_tmpPath);
            Assert.IsTrue(File.Exists(_tmpPath));

            // Round-trip via a fresh asset
            var fresh = ScriptableObject.CreateInstance<FBTCalibrationData>();
            try
            {
                bool ok = fresh.LoadFromJson(_tmpPath);
                Assert.IsTrue(ok);
                Assert.AreEqual(2, fresh.Entries.Count);

                Assert.IsTrue(fresh.TryGet(TrackerSlotKind.Waist, out var rotW, out var posW));
                Assert.AreEqual(Quaternion.Euler(0, 30, 0).x, rotW.x, 1e-5f);

                Assert.IsTrue(fresh.TryGet(TrackerSlotKind.Chest, out _, out var posC));
                Assert.AreEqual(0.05f, posC.y, 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(fresh);
            }
        }

        [Test]
        public void LoadFromJson_MissingFile_ReturnsFalse_NoException()
        {
            string nonexistent = Path.Combine(Path.GetTempPath(), "definitely_not_here.json");
            Assert.IsFalse(_data.LoadFromJson(nonexistent));
        }

        /// <summary>Minimal in-memory provider for capture testing.</summary>
        sealed class MockProvider : ITrackerSourceProvider
        {
            readonly System.Collections.Generic.List<TrackerSlot> _slots = new();
            readonly System.Collections.Generic.Dictionary<TrackerSlotKind, TrackerSlot> _byKind = new();

            public System.Collections.Generic.IReadOnlyList<TrackerSlot> Slots => _slots;
            public bool HasFullBodyTrackers => _slots.Count > 0;

            public TrackerSlot GetSlot(TrackerSlotKind kind) =>
                _byKind.TryGetValue(kind, out var s) ? s : null;

            public void Tick(float deltaTime) { }

            public void AddSlot(TrackerSlotKind kind, Quaternion rotOffset, Vector3 posOffset)
            {
                var go = new GameObject($"mock_{kind}");
                var slot = new TrackerSlot(kind)
                {
                    Source = go.transform,
                    CalibrationRotOffset = rotOffset,
                    CalibrationPosOffset = posOffset,
                    IsTracking = true,
                };
                _slots.Add(slot);
                _byKind[kind] = slot;
            }
        }
    }
}
