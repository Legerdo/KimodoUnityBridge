using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KimodoBridge;
using KimodoBridge.Editor;
using KimodoUnityBridge;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TimelineInject;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace KimodoUnityBridge.Command.Tests
{
    /// <summary>
    /// Generation-facing correspondence checks. These tests stop at the
    /// generated request boundary so they verify the exact payload that would
    /// be sent to the motion backend without requiring a network generation.
    /// </summary>
    public sealed class GenerationConstraintCorrespondenceTests
    {
        private const float PositionToleranceMeters = 0.003f;

        [Test, Explicit("Requires a reachable QuickServer and an installed Kimodo model.")]
        public void RealKModelGeneration_RetargetedClipMatchesRoot2DKeyframe()
        {
            using (var fixture = new GenerationFixture("RealKModelGeneration"))
            {
                KimodoEditorGenerateRequest request = fixture.BuildRequest(
                    fixture.Capture("root2d", 30.0 / 30.0));
                request.Prompt = "a person standing still";
                request.DiffusionSteps = 2;
                request.EffectiveSeed = 1701;

                KimodoBridgeService bridge = KimodoBridgeService.Shared;
                JObject modelStatus;
                try
                {
                    modelStatus = bridge.ListModelConfigurationsAsync(
                        KimodoMotionModelProfiles.DefaultModelName,
                        KimodoTextEncoderModeProtocol.HighPerformance,
                        request.ModelsRoot,
                        null,
                        CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    Assert.Ignore($"KModel runtime is unavailable: {exception.Message}");
                    return;
                }

                if (!ModelStatusContainsUsableModel(modelStatus, KimodoMotionModelProfiles.DefaultModelName))
                {
                    Assert.Ignore("The configured QuickServer does not report a usable default Kimodo model.");
                    return;
                }

                KimodoEditorGenerationResult result = null;
                try
                {
                    result = KimodoEditorGeneratePipeline.ExecuteAsync(request).GetAwaiter().GetResult();
                    Assert.That(result, Is.Not.Null);
                    Assert.That(result.GeneratedClip, Is.Not.Null);

                    Assert.That(
                        KimodoRetargetSamplingUtility.SampleBoneClipToBoneSample(
                            result.GeneratedClip,
                            fixture.Skeleton,
                            1.0,
                            KimodoRetargetClipSamplingUtility.ResolveClipSamplingMode(result.GeneratedClip),
                            out BoneSample actualBone,
                            out string sampleError),
                        Is.True,
                        sampleError);
                    Assert.That(
                        KimodoRetargetSamplingUtility.TryApplyBoneSampleToRetargetSkeleton(
                            actualBone,
                            fixture.Skeleton,
                            out sampleError),
                        Is.True,
                        sampleError);
                    var actual = new KimodoMarkerSampleResult
                    {
                        rootOverride = KimodoRigidTransform.Identity,
                        effectors = new KimodoConstraintEffectors(),
                        enableMask = new KimodoConstraintMask(),
                        validMask = new KimodoConstraintMask()
                    };
                    KimodoRetargetMarkerSamplingUtility.CaptureWorldTargets(fixture.Skeleton, actual);

                    KimodoMarkerSampleResult expected = request.ConstraintSamples.Single();
                    Assert.That(Vector3.Distance(actual.rootOverride.t, expected.rootOverride.t),
                        Is.LessThanOrEqualTo(PositionToleranceMeters),
                        $"KModel retargeted root differs by {Vector3.Distance(actual.rootOverride.t, expected.rootOverride.t):F4}m.");
                }
                finally
                {
                    request.CleanupGeneratedClips();
                    if (result?.GeneratedClip != null && result.GeneratedClip != request.TargetClip)
                    {
                        Object.DestroyImmediate(result.GeneratedClip);
                    }
                }
            }
        }

        [Test]
        public void GenerationRequest_DifferentConstraintFamilies_PreserveFramesAndCoordinates()
        {
            using (var fixture = new GenerationFixture("GenerationConstraintFamilies"))
            {
                KimodoMarkerSampleResult fullBody = fixture.Capture("fullbody", 10.0 / 30.0);
                KimodoMarkerSampleResult root2D = fixture.Capture("root2d", 25.0 / 30.0);
                KimodoMarkerSampleResult rootEffect = fixture.Capture("root-effect", 40.0 / 30.0);
                rootEffect.enableMask = new KimodoConstraintMask
                {
                    rootPosition = true,
                    rootHeading = true,
                    rightHand = true
                };
                rootEffect.validMask = rootEffect.enableMask.Clone();

                KimodoEditorGenerateRequest request = fixture.BuildRequest(
                    fullBody,
                    root2D,
                    rootEffect);
                JArray exported = JArray.Parse(request.Constraints.json);

                JObject fullBodyJson = Find(exported, "fullbody");
                JObject root2DJson = Find(exported, "root2d");
                JObject rightHandJson = Find(exported, "right-hand");

                CollectionAssert.AreEqual(new[] { 10 }, Frames(fullBodyJson));
                CollectionAssert.Contains(Frames(root2DJson), 25,
                    "The explicit root2d target frame must remain present.");
                CollectionAssert.Contains(Frames(rightHandJson), 40,
                    "The root-effect target frame must remain present even when the composer carries a support frame.");

                AssertVector3(fullBodyJson["root_positions"][0],
                    ToKimodoPosition(fullBody.rootOverride.t), "fullbody root", PositionToleranceMeters);
                int root2DTargetRow = Array.IndexOf(Frames(root2DJson), 25);
                Assert.That(root2DTargetRow, Is.GreaterThanOrEqualTo(0));
                AssertVector2(root2DJson["smooth_root_2d"][root2DTargetRow],
                    new Vector2(-root2D.rootOverride.t.x, root2D.rootOverride.t.z),
                    "root2d root", PositionToleranceMeters);
                int rightHandTargetRow = Array.IndexOf(Frames(rightHandJson), 40);
                Assert.That(rightHandTargetRow, Is.GreaterThanOrEqualTo(0));
                AssertVector3(rightHandJson["target_positions"][rightHandTargetRow],
                    ToKimodoPosition(rootEffect.effectors.rightHand.t),
                    "root-effect right hand", PositionToleranceMeters);
            }
        }

        [Test]
        public void GenerationRequest_RootEffect_EmitsRootAndEffectorAtOneFrame()
        {
            using (var fixture = new GenerationFixture("GenerationRootEffect"))
            {
                KimodoMarkerSampleResult rootEffect = fixture.Capture("root-effect", 32.0 / 30.0);
                rootEffect.enableMask = new KimodoConstraintMask
                {
                    rootPosition = true,
                    rootHeading = true,
                    leftHand = true,
                    rightHand = true
                };
                rootEffect.validMask = rootEffect.enableMask.Clone();

                KimodoEditorGenerateRequest request = fixture.BuildRequest(rootEffect);
                JArray exported = JArray.Parse(request.Constraints.json);
                JObject root = Find(exported, "root2d");
                JObject leftHand = Find(exported, "left-hand");
                JObject rightHand = Find(exported, "right-hand");

                CollectionAssert.AreEqual(new[] { 32 }, Frames(root));
                CollectionAssert.AreEqual(new[] { 32 }, Frames(leftHand));
                CollectionAssert.AreEqual(new[] { 32 }, Frames(rightHand));
                Assert.That(request.ConstraintSamples, Has.Count.EqualTo(1));
                AssertVector3(leftHand["target_positions"][0],
                    ToKimodoPosition(rootEffect.effectors.leftHand.t),
                    "root-effect left hand", PositionToleranceMeters);
                AssertVector3(rightHand["target_positions"][0],
                    ToKimodoPosition(rootEffect.effectors.rightHand.t),
                    "root-effect right hand", PositionToleranceMeters);
            }
        }

        [Test]
        public void GenerationRequest_ConstraintSampleOrderDoesNotChangeFrameCorrespondence()
        {
            using (var fixture = new GenerationFixture("GenerationConstraintOrder"))
            {
                KimodoMarkerSampleResult fullBody = fixture.Capture("fullbody", 12.0 / 30.0);
                KimodoMarkerSampleResult root2D = fixture.Capture("root2d", 48.0 / 30.0);

                KimodoEditorGenerateRequest request = fixture.BuildRequest(root2D, fullBody);
                JArray exported = JArray.Parse(request.Constraints.json);

                CollectionAssert.AreEqual(new[] { 12 }, Frames(Find(exported, "fullbody")));
                CollectionAssert.AreEqual(new[] { 48 }, Frames(Find(exported, "root2d")));
            }
        }

        private static JObject Find(JArray values, string type)
        {
            JObject result = values
                .OfType<JObject>()
                .SingleOrDefault(value => string.Equals(value.Value<string>("type"), type, StringComparison.Ordinal));
            Assert.That(result, Is.Not.Null, $"Expected generated constraint type '{type}'. Payload: {values}");
            return result;
        }

        private static bool ModelStatusContainsUsableModel(JObject status, string modelName)
        {
            if (status == null) return false;
            foreach (JToken token in status.Descendants())
            {
                if (token.Type != JTokenType.String) continue;
                string value = token.Value<string>();
                if (string.Equals(value, modelName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "ready", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "available", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static int[] Frames(JObject value)
        {
            return value["frame_indices"]?.Values<int>().ToArray() ?? Array.Empty<int>();
        }

        private static Vector3 ToKimodoPosition(Vector3 unityPosition)
        {
            return new Vector3(-unityPosition.x, unityPosition.y, unityPosition.z);
        }

        private static void AssertVector2(JToken actual, Vector2 expected, string label, float tolerance)
        {
            Assert.That(actual, Is.TypeOf<JArray>(), label);
            JArray values = (JArray)actual;
            Assert.That(values.Count, Is.GreaterThanOrEqualTo(2), label);
            Assert.That(Mathf.Abs(values[0].Value<float>() - expected.x), Is.LessThanOrEqualTo(tolerance), label + " x");
            Assert.That(Mathf.Abs(values[1].Value<float>() - expected.y), Is.LessThanOrEqualTo(tolerance), label + " y");
        }

        private static void AssertVector3(JToken actual, Vector3 expected, string label, float tolerance)
        {
            Assert.That(actual, Is.TypeOf<JArray>(), label);
            JArray values = (JArray)actual;
            Assert.That(values.Count, Is.GreaterThanOrEqualTo(3), label);
            Assert.That(Mathf.Abs(values[0].Value<float>() - expected.x), Is.LessThanOrEqualTo(tolerance), label + " x");
            Assert.That(Mathf.Abs(values[1].Value<float>() - expected.y), Is.LessThanOrEqualTo(tolerance), label + " y");
            Assert.That(Mathf.Abs(values[2].Value<float>() - expected.z), Is.LessThanOrEqualTo(tolerance), label + " z");
        }

        private sealed class GenerationFixture : IDisposable
        {
            private readonly GameObject directorObject;
            private readonly TimelineAsset timeline;
            private readonly PlayableDirector director;
            private readonly AnimationTrack track;
            private readonly TimelineClip timelineClip;
            private readonly KimodoPlayableClip playable;
            private readonly RetargetSkeleton skeleton;

            internal GenerationFixture(string name)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                directorObject = new GameObject(name + "Director");
                director = directorObject.AddComponent<PlayableDirector>();
                director.playableAsset = timeline;
                track = timeline.CreateTrack<AnimationTrack>(null, name + "Track");
                timelineClip = track.CreateClip<KimodoPlayableClip>();
                timelineClip.duration = 2.0;

                Assert.That(KimodoRuntimeAvatarSkeletonBuilder.TryLoadAvatarByModelName(
                    KimodoMotionModelProfiles.DefaultModelName,
                    out Avatar avatar,
                    out string error), Is.True, error);
                Assert.That(KimodoRetargetAvatarUtility.TryBuildRetargetSkeleton(
                    avatar,
                    name + "Skeleton",
                    out skeleton,
                    out error), Is.True, error);
                director.SetGenericBinding(track, skeleton.animator);
                playable = (KimodoPlayableClip)timelineClip.asset;
                playable.bridgeModelName = KimodoMotionModelProfiles.DefaultModelName;
                playable.inOutConstraintMode = KimodoInOutConstraintMode.None;
                playable.autoBeginAnchor = false;
                playable.generateLoop = false;
                director.RebuildGraph();
                director.Evaluate();
            }

            internal KimodoMarkerSampleResult Capture(string mode, double sampleTime)
            {
                Assert.That(KimodoRetargetSamplingUtility.TryCaptureMuscleSample(
                    skeleton,
                    out MuscleSample pose,
                    out string error), Is.True, error);
                var sample = new KimodoMarkerSampleResult
                {
                    constraintMode = mode,
                    sampleTime = sampleTime,
                    sampleData = pose.Clone(),
                    enableMask = KimodoConstraintMask.ForType(mode),
                    validMask = KimodoConstraintMask.ForType(mode),
                    effectors = new KimodoConstraintEffectors(),
                    rootOverride = KimodoRigidTransform.Identity,
                    enabled = true
                };
                KimodoRetargetMarkerSamplingUtility.CaptureWorldTargets(skeleton, sample);
                sample.enableMask = KimodoConstraintMask.ForType(mode);
                sample.validMask = sample.enableMask.Clone();
                return sample;
            }

            internal KimodoEditorGenerateRequest BuildRequest(params KimodoMarkerSampleResult[] samples)
            {
                var external = new KimodoExternalConstraintRequest
                {
                    Enabled = true,
                    IncludeTimelineConstraints = false,
                    ConstraintSamples = new List<KimodoMarkerSampleResult>(samples)
                };
                return KimodoPlayableClipGenerationHostService.BuildRequest(
                    playable,
                    "constraint correspondence test",
                    external,
                    CancellationToken.None,
                    timelineClipOverride: timelineClip);
            }

            internal RetargetSkeleton Skeleton => skeleton;

            public void Dispose()
            {
                skeleton?.Dispose();
                Object.DestroyImmediate(directorObject);
                Object.DestroyImmediate(timeline);
            }
        }
    }
}
