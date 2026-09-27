using System;
using System.Collections.Generic;
using System.Reflection;
using KimodoBridge;
using KimodoBridge.Editor;
using KimodoUnityBridge.Command;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace KimodoUnityBridge.Command.Tests
{
    public sealed class KimodoRoot2DCommandEquivalenceTests
    {
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        [Test]
        public void CommandRoot2D_WithPoseReferenceMatchesRightClickRoot2DShape()
        {
            var directorObject = new GameObject("Root2DEquivalenceDirector");
            var director = directorObject.AddComponent<PlayableDirector>();
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var animationTrack = timeline.CreateTrack<AnimationTrack>(null, "Character");
            var poseTrack = timeline.CreateTrack<AnimationTrack>(animationTrack, "Character.Poses");
            var poseMarker = poseTrack.CreateMarker<KimodoConstraintMarker>(0.0);
            var marker = animationTrack.CreateMarker<KimodoConstraintMarker>(1.5);
            FieldInfo currentSessionField = typeof(command_context).GetField("currentTimelineSession", PrivateStatic);
            object previousSession = currentSessionField.GetValue(null);
            try
            {
                var support = CreateSupportSample();
                poseMarker.MarkerType = KimodoConstraintMarkerType.External;
                poseMarker.autoSample = false;
                poseMarker.constraintEnabled = false;
                poseMarker.SampleData = support;
                poseMarker.CommitSampleData();

                marker.ConstraintMode = KimodoConstraintMode.Root2D;
                marker.autoSample = true;
                marker.constraintEnabled = true;
                marker.SampleData = new KimodoMarkerSampleResult();
                marker.CommitSampleData();

                var character = new command_context.TimelineCharacterRecord(
                    "Character", directorObject, null, null, animationTrack, poseTrack, string.Empty);
                var sessionType = typeof(command_context).GetNestedType(
                    "TimelineSessionRecord", BindingFlags.NonPublic);
                var session = Activator.CreateInstance(sessionType, new object[]
                {
                    Guid.NewGuid(), "Root2DEquivalence", director, timeline, string.Empty,
                    false, null, directorObject
                });
                ((List<command_context.TimelineCharacterRecord>)sessionType
                    .GetProperty("Characters")
                    .GetValue(session)).Add(character);
                currentSessionField.SetValue(null, session);

                MethodInfo build = typeof(command_context).GetMethod(
                    "BuildRoot2DConstraint", PrivateStatic);
                var commandValue = JObject.Parse(
                    "{'pose':{'track':'Character.Poses','index':0}} ");
                var commandResult = (KimodoMarkerSampleResult)build.Invoke(
                    null, new object[] { commandValue, 1.5, 0 });

                KimodoMarkerSampleResult rightClickResult =
                    KimodoMarkerSamplingUtility.NormalizeConstraintMarkerSample(marker, support);

                Assert.That(commandResult.constraintMode, Is.EqualTo(rightClickResult.constraintMode));
                Assert.That(commandResult.sampleTime, Is.EqualTo(rightClickResult.sampleTime).Within(1e-8));
                Assert.That(commandResult.enabled, Is.EqualTo(rightClickResult.enabled));
                AssertMaskEqual(commandResult.enableMask, rightClickResult.enableMask);
                AssertMaskEqual(commandResult.validMask, rightClickResult.validMask);
                AssertEffectorsEqual(commandResult.effectors, rightClickResult.effectors);
                Assert.That(commandResult.carryEffectorsWithRoot,
                    Is.EqualTo(rightClickResult.carryEffectorsWithRoot));
                Assert.That(Vector3.Distance(commandResult.rootOverride.t, rightClickResult.rootOverride.t), Is.LessThan(1e-5f));
                Assert.That(Quaternion.Angle(commandResult.rootOverride.q, rightClickResult.rootOverride.q), Is.LessThan(1e-4f));
                CollectionAssert.AreEqual(support.sampleData.data, commandResult.sampleData.data);
            }
            finally
            {
                currentSessionField.SetValue(null, previousSession);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(directorObject);
            }
        }

        [Test]
        public void CommandRoot2D_WithoutPoseReferenceIsRejected()
        {
            MethodInfo build = typeof(command_context).GetMethod(
                "BuildRoot2DConstraint", PrivateStatic);
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
                build.Invoke(null, new object[]
                {
                    JObject.Parse("{'position':[1,2],'heading':[0,1]}"),
                    1.0,
                    0
                }));

            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(exception.InnerException.Message, Does.Contain("root2d.pose is required"));
        }

        private static KimodoMarkerSampleResult CreateSupportSample()
        {
            var sample = new KimodoMarkerSampleResult
            {
                constraintMode = "fullbody",
                sampleTime = 0.25,
                enableMask = KimodoConstraintMask.ForType("fullbody"),
                validMask = KimodoConstraintMask.ForType("fullbody"),
                rootOverride = new KimodoRigidTransform
                {
                    t = new Vector3(2.25f, 0.84f, -1.75f),
                    q = Quaternion.Euler(11f, 37f, -6f)
                }
            };
            sample.sampleData = new KimodoBridge.MuscleSample();
            for (int i = 0; i < sample.sampleData.data.Length; i++)
            {
                sample.sampleData.data[i] = (i + 1) * 0.001f;
            }
            return sample;
        }

        private static void AssertMaskEqual(KimodoConstraintMask actual, KimodoConstraintMask expected)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(expected, Is.Not.Null);
            Assert.That(actual.muscle, Is.EqualTo(expected.muscle));
            Assert.That(actual.rootTQ, Is.EqualTo(expected.rootTQ));
            Assert.That(actual.leftFootTQ, Is.EqualTo(expected.leftFootTQ));
            Assert.That(actual.rightFootTQ, Is.EqualTo(expected.rightFootTQ));
            Assert.That(actual.rootPosition, Is.EqualTo(expected.rootPosition));
            Assert.That(actual.rootHeading, Is.EqualTo(expected.rootHeading));
            Assert.That(actual.leftFoot, Is.EqualTo(expected.leftFoot));
            Assert.That(actual.rightFoot, Is.EqualTo(expected.rightFoot));
            Assert.That(actual.leftHand, Is.EqualTo(expected.leftHand));
            Assert.That(actual.rightHand, Is.EqualTo(expected.rightHand));
        }

        private static void AssertEffectorsEqual(
            KimodoConstraintEffectors actual,
            KimodoConstraintEffectors expected)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(expected, Is.Not.Null);
            AssertRigidTransformEqual(actual.leftFoot, expected.leftFoot);
            AssertRigidTransformEqual(actual.rightFoot, expected.rightFoot);
            AssertRigidTransformEqual(actual.leftHand, expected.leftHand);
            AssertRigidTransformEqual(actual.rightHand, expected.rightHand);
        }

        private static void AssertRigidTransformEqual(
            KimodoRigidTransform actual,
            KimodoRigidTransform expected)
        {
            Assert.That(Vector3.Distance(actual.t, expected.t), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(actual.q, expected.q), Is.LessThan(1e-4f));
        }
    }
}
