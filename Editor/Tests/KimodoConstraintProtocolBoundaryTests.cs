using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KimodoBridge.Editor.Tests
{
    public sealed class KimodoConstraintProtocolBoundaryTests
    {
        [Test]
        public void PayloadSerialization_RejectsSameFrameRoot2DAndFullBody()
        {
            var payload = new KimodoConstraintPayload
            {
                json = new JArray(
                    new JObject
                    {
                        ["type"] = "root2d",
                        ["frame_indices"] = new JArray(4),
                        ["smooth_root_2d"] = new JArray { new JArray { 1f, 2f } }
                    },
                    new JObject
                    {
                        ["type"] = "fullbody",
                        ["frame_indices"] = new JArray(4)
                    }).ToString()
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                payload.Serialize(KimodoMotionModelProfiles.DefaultModelName, new List<byte[]>()));

            Assert.That(exception.Message, Does.Contain("Undefined behavior"));
            Assert.That(exception.Message, Does.Contain("frame 4"));
        }

        [Test]
        public void PayloadSerialization_PreservesRoot2DAndFullBodyAtDifferentFrames()
        {
            var payload = new KimodoConstraintPayload
            {
                json = new JArray(
                    new JObject
                    {
                        ["type"] = "root2d",
                        ["frame_indices"] = new JArray(4),
                        ["smooth_root_2d"] = new JArray { new JArray { 1f, 2f } }
                    },
                    new JObject
                    {
                        ["type"] = "fullbody",
                        ["frame_indices"] = new JArray(5)
                    }).ToString()
            };

            JArray serialized = JArray.Parse(payload.Serialize(
                KimodoMotionModelProfiles.DefaultModelName,
                new List<byte[]>()));

            Assert.That(serialized, Has.Count.EqualTo(2));
            Assert.That(serialized[0]["frame_indices"][0].Value<int>(), Is.EqualTo(4));
            Assert.That(serialized[1]["frame_indices"][0].Value<int>(), Is.EqualTo(5));
        }
    }
}
