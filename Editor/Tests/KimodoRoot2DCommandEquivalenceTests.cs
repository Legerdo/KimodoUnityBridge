using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KimodoBridge;
using KimodoBridge.Editor;
using KimodoUnityBridge.Command;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
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
                Vector3 supportRoot = support.rootOverride.t;
                Vector3 supportForward = support.rootOverride.q * Vector3.forward;
                var commandValue = new JObject
                {
                    ["pose"] = new JObject { ["track"] = "Character.Poses", ["index"] = 0 },
                    ["root2d"] = new JObject
                    {
                        ["position"] = new JArray(supportRoot.x, supportRoot.z),
                        ["heading"] = new JArray(supportForward.x, supportForward.z)
                    }
                };
                var commandResult = (KimodoMarkerSampleResult)build.Invoke(
                    null, new object[] { commandValue, 1.5, 0, null, null, new List<string>() });

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
        public void CommandRoot2D_WithoutPoseReferenceUsesAvatarRestPose()
        {
            Assert.That(KimodoRuntimeAvatarSkeletonBuilder.TryLoadAvatarByModelName(
                KimodoMotionModelProfiles.DefaultModelName,
                out Avatar avatar,
                out string avatarError), Is.True, avatarError);
            Assert.That(KimodoRetargetAvatarUtility.TryBuildRetargetSkeleton(
                avatar,
                "Root2DAvatarRestFixture",
                out RetargetSkeleton skeleton,
                out string skeletonError), Is.True, skeletonError);
            MethodInfo build = typeof(command_context).GetMethod(
                "BuildRoot2DConstraint", PrivateStatic);
            try
            {
                var warnings = new List<string>();
                var result = (KimodoMarkerSampleResult)build.Invoke(null, new object[]
                {
                    JObject.Parse("{'root2d':{'position':[1,2],'heading':[0,1]}}"),
                    1.0,
                    0,
                    skeleton,
                    null,
                    warnings
                });

                Assert.That(result.constraintMode, Is.EqualTo("root2d"));
                Assert.That(result.sampleData, Is.Not.Null);
                Assert.That(result.sampleData.IsValid, Is.True);
                Assert.That(result.rootOverride.t.x, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(result.rootOverride.t.z, Is.EqualTo(2f).Within(1e-5f));
                Assert.That(warnings, Has.Count.EqualTo(1));
                Assert.That(warnings[0], Does.Contain("Avatar skeleton rest pose"));
            }
            finally
            {
                skeleton.Dispose();
            }
        }

        [Test]
        public void CommandContext_ExposesOptionalPoseAndEffectorValidationSchema()
        {
            JObject definitions = JObject.Parse(command_dispatcher.GetCommandDefinitionsJson());
            JObject effector = null;
            foreach (JObject tool in (JArray)definitions["tools"])
            {
                if (string.Equals(tool.Value<string>("name"), "effector_validate", StringComparison.Ordinal))
                {
                    effector = tool;
                    break;
                }
            }

            Assert.That(effector, Is.Not.Null);
            JObject properties = (JObject)effector["inputSchema"]["properties"];
            IEnumerable<string> required = ((JArray)effector["inputSchema"]["required"]).Values<string>();
            CollectionAssert.Contains(required, "character");
            CollectionAssert.Contains(required, "effector");
            CollectionAssert.Contains(required, "target");
            CollectionAssert.DoesNotContain(required, "pose");
            Assert.That(properties["pose"], Is.Not.Null);
            Assert.That(properties["root2d"], Is.Not.Null);
            JObject constraints = JObject.Parse((string)typeof(command_context)
                .GetMethod("BuildCommandDefinitionsJson", PrivateStatic)
                .Invoke(null, null));
            JObject generate = ((JArray)constraints["tools"]).Values<JObject>()
                .Single(item => item.Value<string>("name") == "kimodo_generate_animation");
            string serialized = generate.ToString();
            Assert.That(serialized, Does.Contain("root_effect"));
            Assert.That(serialized, Does.Contain("effect"));
            Assert.That(serialized, Does.Contain("\"type\""));
        }

        [Test]
        public void GeneratedConstraintSchema_HasStrictTypedBranches()
        {
            JObject definitions = JObject.Parse((string)typeof(command_context)
                .GetMethod("BuildCommandDefinitionsJson", PrivateStatic)
                .Invoke(null, null));
            JObject generate = ((JArray)definitions["tools"]).Values<JObject>()
                .Single(item => item.Value<string>("name") == "kimodo_generate_animation");
            JObject constraints = (JObject)generate["inputSchema"]["properties"]["constraints"];
            JArray branches = (JArray)constraints["items"]["oneOf"];

            JObject fullBody = branches.Values<JObject>().Single(item =>
                item["properties"]?["type"]?["const"]?.Value<string>() == "fullbody");
            JObject root2D = branches.Values<JObject>().Single(item =>
                item["properties"]?["type"]?["const"]?.Value<string>() == "root2d");
            JObject rootEffect = branches.Values<JObject>().Single(item =>
                item["properties"]?["type"]?["const"]?.Value<string>() == "root_effect");

            Assert.That(fullBody["properties"]["pose"], Is.Not.Null);
            Assert.That(fullBody["properties"]["root2d"], Is.Null);
            Assert.That(fullBody["properties"]["effect"], Is.Null);
            Assert.That(root2D["properties"]["root2d"], Is.Not.Null);
            Assert.That(root2D["properties"]["effect"], Is.Null);
            Assert.That(rootEffect["properties"]["root2d"], Is.Not.Null);
            Assert.That(rootEffect["properties"]["effect"], Is.Not.Null);
        }

        [Test]
        public void ConstraintManual_UsesTopLevelTypedProtocol()
        {
            JObject manual = (JObject)typeof(command_context)
                .GetMethod("BuildConstraintManual", PrivateStatic)
                .Invoke(null, null);
            JArray entries = (JArray)manual["constraints"];
            JObject fullBody = entries.Values<JObject>().Single(item => item.Value<string>("type") == "fullbody");
            JObject rootEffect = entries.Values<JObject>().Single(item => item.Value<string>("type") == "root_effect");
            Assert.That(fullBody["shape"]["fullbody"], Is.Null);
            Assert.That(fullBody["shape"]["pose"], Is.Not.Null);
            Assert.That(rootEffect["shape"]["root2d"], Is.Not.Null);
            Assert.That(rootEffect["shape"]["effect"], Is.Not.Null);
        }

        [Test]
        public void PointConstraintParser_RejectsCrossTypeFields()
        {
            MethodInfo validate = typeof(command_context).GetMethod(
                "ValidatePointConstraintShape", PrivateStatic);
            Assert.That(validate, Is.Not.Null);

            var root2DWithEffect = new JObject
            {
                ["frame"] = 10,
                ["type"] = "root2d",
                ["root2d"] = new JObject
                {
                    ["position"] = new JArray(0, 0),
                    ["heading"] = new JArray(0, 1)
                },
                ["effect"] = new JObject
                {
                    ["right_hand"] = new JObject { ["position"] = new JArray(0, 1, 0) }
                }
            };
            Assert.Throws<TargetInvocationException>(() => validate.Invoke(
                null, new object[] { root2DWithEffect, "root2d", 0 }));

            var fullBodyWithRoot = new JObject
            {
                ["frame"] = 10,
                ["type"] = "fullbody",
                ["pose"] = new JObject { ["track"] = "Pose", ["index"] = 0 },
                ["root2d"] = new JObject
                {
                    ["position"] = new JArray(0, 0),
                    ["heading"] = new JArray(0, 1)
                }
            };
            Assert.Throws<TargetInvocationException>(() => validate.Invoke(
                null, new object[] { fullBodyWithRoot, "fullbody", 1 }));
        }

        [Test]
        public void StaticHelp_MatchesCodeGeneratedDefinitions()
        {
            TextAsset help = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Packages/com.unity.kimodo_unity_motion_tools/Command/help.json");
            Assert.That(help, Is.Not.Null);
            JObject generated = JObject.Parse(command_dispatcher.GetCommandDefinitionsJson());
            JObject committed = JObject.Parse(help.text);
            Assert.That(JToken.DeepEquals(committed, generated), Is.True,
                "Command/help.json must be exported from the code-generated definitions.");
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
