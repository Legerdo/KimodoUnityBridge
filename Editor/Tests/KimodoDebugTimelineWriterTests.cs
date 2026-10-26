using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace KimodoBridge.Editor.Tests
{
    public sealed class KimodoDebugTimelineWriterTests
    {
        [Test]
        public void TryResolveWindowPlaybackTiming_MapsFrameCountThroughTimeScale()
        {
            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AnimationTrack track = timeline.CreateTrack<AnimationTrack>();
            TimelineClip source = track.CreateClip<AnimationPlayableAsset>();
            source.start = 3.0;
            source.duration = 10.0;
            source.clipIn = 0.5;
            source.timeScale = 2.0;
            try
            {
                Assert.That(
                    KimodoDebugTimelineWriter.TryResolveWindowPlaybackTiming(
                        source,
                        requestFrameCount: 60,
                        frameRate: 30f,
                        out double duration,
                        out double timeScale),
                    Is.True);
                Assert.That(duration, Is.EqualTo(1.0));
                Assert.That(timeScale, Is.EqualTo(2.0));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void TryPrepare_AppendsBoundPlaceholderOnPerGenerationDebugTrackAndMutesPreviousDebugTracks()
        {
            KimodoPlayableClipGenerationSettings settings = KimodoPlayableClipGenerationSettings.instance;
            bool previousSetting = settings.WriteResampledTimelineCacheClips;
            Scene testScene = SceneManager.GetActiveScene();
            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            TimelineClip sourceClip = null;
            KimodoEditorGenerateRequest request = null;
            KimodoEditorGenerateRequest secondRequest = null;
            KimodoEditorGenerateRequest independentRequest = null;
            GameObject directorObject = null;
            GameObject profileBinding = null;
            GameObject sourceBinding = null;
            try
            {
                settings.WriteResampledTimelineCacheClips = true;
                directorObject = new GameObject("Debug Timeline Test Director");
                SceneManager.MoveGameObjectToScene(directorObject, testScene);
                PlayableDirector director = directorObject.AddComponent<PlayableDirector>();
                director.playableAsset = timeline;

                AnimationTrack oldDebugTrack = timeline.CreateTrack<AnimationTrack>(null, "debug track");
                oldDebugTrack.muted = false;
                KimodoDebugRollTrack oldRollTrack = timeline.CreateTrack<KimodoDebugRollTrack>(null, "DebugRoll_legacy");
                oldRollTrack.muted = false;
                AnimationTrack sourceTrack = timeline.CreateTrack<AnimationTrack>(null, "Source");
                sourceBinding = new GameObject("Formal Source Character");
                SceneManager.MoveGameObjectToScene(sourceBinding, testScene);
                Animator sourceAnimator = sourceBinding.AddComponent<Animator>();
                director.SetGenericBinding(sourceTrack, sourceAnimator);
                profileBinding = CreateProfileBinding(
                    KimodoMotionModelProfiles.DefaultModelName,
                    testScene,
                    out Animator profileAnimator);
                sourceClip = sourceTrack.CreateClip<KimodoPlayableClip>();
                sourceClip.start = 4.0;
                sourceClip.duration = 2.0;
                sourceClip.clipIn = 0.25;
                sourceClip.timeScale = 1.25;
                KimodoPlayableClip sourceAsset = (KimodoPlayableClip)sourceClip.asset;
                request = new KimodoEditorGenerateRequest
                {
                    ModelName = KimodoMotionModelProfiles.DefaultModelName,
                    TargetFrameCount = 60,
                    TargetFrameRate = 30f,
                    TimelineClipSnapshot = sourceClip
                };

                KimodoDebugTimelineWriter.BeginGeneration(sourceClip);
                sourceAsset.generationOutputMode = KimodoGenerationOutputMode.ModelBone;
                KimodoEditorGenerateOutputPlan outputPlanSnapshot = KimodoTimelineGenerationOutputPlanner.Capture(
                    sourceAsset,
                    explicitRetargetAvatar: null,
                    KimodoMotionModelProfiles.DefaultModelName,
                    profileBinding);
                AnimationClip generatedProfileClip = new AnimationClip();
                try
                {
                    KimodoEditorGenerateOutputPlan outputPlan = KimodoTimelineGenerationOutputPlanner.Resolve(
                        outputPlanSnapshot,
                        profileBinding,
                        generatedProfileClip,
                        KimodoMotionModelProfiles.DefaultModelName);
                    Assert.That(outputPlan.SkipRetarget, Is.True,
                        "The profile Animator binding must be captured before the formal output plan is resolved.");
                }
                finally
                {
                    Object.DestroyImmediate(generatedProfileClip);
                }
                KimodoDebugTimelineWriter.TryPrepare(sourceAsset, request, "Generation Pass");

                Assert.That(oldDebugTrack.muted, Is.True);
                Assert.That(oldRollTrack.muted, Is.True);
                Assert.That(request.DebugTimelineReservation, Is.Not.Null);
                KimodoDebugTimelineReservation reservation = request.DebugTimelineReservation;
                Assert.That(request.DebugTimelineGroup.Timeline, Is.SameAs(timeline));
                Assert.That(request.DebugTimelineGroup.Track, Is.Not.SameAs(sourceTrack));
                Assert.That(request.DebugTimelineGroup.Track.name, Does.StartWith("debug track"));
                Assert.That(timeline.GetOutputTracks().Count(), Is.EqualTo(5));
                Assert.That(director.GetGenericBinding(request.DebugTimelineGroup.Track), Is.SameAs(request.DebugTimelineGroup.Animator));
                Assert.That(director.GetGenericBinding(sourceTrack), Is.SameAs(sourceAnimator),
                    "The formal animation track binding must remain unchanged.");
                Assert.That(request.DebugTimelineGroup.ProfileInstance.GetComponentsInChildren<Animator>(true)
                    .All(item => item.avatar == null), Is.True,
                    "Debug profile bindings must stay generic and must not use a Humanoid Avatar.");
                Assert.That(request.DebugTimelineGroup.ProfileInstance.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(reservation.TimelineClip.asset, Is.TypeOf<KimodoPlayableClip>());
                Assert.That(((KimodoPlayableClip)reservation.TimelineClip.asset).clip, Is.Null);
                Assert.That(((KimodoPlayableClip)reservation.TimelineClip.asset).removeStartOffset, Is.False);
                KimodoDebugTimelineReservation overview = request.DebugTimelineGroup.OverviewReservations.Single();
                Assert.That(overview.IsOverview, Is.True);
                Assert.That(overview.TimelineClip.start, Is.EqualTo(sourceClip.start));
                Assert.That(overview.TimelineClip.duration, Is.EqualTo(sourceClip.duration));
                Assert.That(((KimodoPlayableClip)overview.TimelineClip.asset).debugPassName,
                    Is.EqualTo("Raw Overview"));
                Assert.That(reservation.TimelineClip.start,
                    Is.EqualTo(overview.TimelineClip.end + 5.0 / timeline.editorSettings.frameRate));
                Assert.That(reservation.TimelineClip.duration, Is.GreaterThan(0.0));
                Assert.That(reservation.TimelineClip.clipIn, Is.EqualTo(0.0));
                Assert.That(reservation.TimelineClip.timeScale, Is.EqualTo(sourceClip.timeScale));
                Assert.That(((KimodoPlayableClip)reservation.TimelineClip.asset).isDebugRawClip, Is.True);
                Assert.That(testScene.GetRootGameObjects().Any(item => item.name.StartsWith("KimodoSession_")), Is.False);

                secondRequest = new KimodoEditorGenerateRequest
                {
                    ModelName = KimodoMotionModelProfiles.DefaultModelName,
                    TargetFrameCount = 60,
                    TargetFrameRate = 30f,
                    TimelineClipSnapshot = sourceClip
                };
                secondRequest.DebugTimelineGroup = request.DebugTimelineGroup;
                KimodoDebugTimelineWriter.TryPrepare(sourceAsset, secondRequest, "Trajectory Pass 2");
                Assert.That(secondRequest.DebugTimelineReservation, Is.Not.Null);
                Assert.That(secondRequest.DebugTimelineGroup.Track, Is.SameAs(request.DebugTimelineGroup.Track));
                Assert.That(secondRequest.DebugTimelineGroup.ProfileInstance,
                    Is.SameAs(request.DebugTimelineGroup.ProfileInstance));
                Assert.That(
                    secondRequest.DebugTimelineReservation.TimelineClip.start,
                    Is.EqualTo(reservation.TimelineClip.end + 5.0 / timeline.editorSettings.frameRate));
                Assert.That(request.DebugTimelineGroup.OverviewReservations.Single().TimelineClip,
                    Is.SameAs(overview.TimelineClip));
                Assert.That(request.DebugTimelineGroup.Track.GetClips().Count(), Is.EqualTo(3),
                    "The shared Debug track should contain one overview and one placeholder per pass.");
                Assert.That(timeline.GetOutputTracks().Count(), Is.EqualTo(5));

                independentRequest = new KimodoEditorGenerateRequest
                {
                    ModelName = KimodoMotionModelProfiles.DefaultModelName,
                    TargetFrameCount = 60,
                    TargetFrameRate = 30f,
                    TimelineClipSnapshot = sourceClip
                };
                KimodoDebugTimelineWriter.TryPrepare(sourceAsset, independentRequest, "Separate Generation");
                Assert.That(independentRequest.DebugTimelineReservation, Is.Not.Null);
                Assert.That(independentRequest.DebugTimelineGroup.Track,
                    Is.Not.SameAs(request.DebugTimelineGroup.Track));
                Assert.That(independentRequest.DebugTimelineGroup.ProfileInstance,
                    Is.Not.SameAs(request.DebugTimelineGroup.ProfileInstance));
                Assert.That(timeline.GetOutputTracks().Count(), Is.EqualTo(7));
            }
            finally
            {
                settings.WriteResampledTimelineCacheClips = previousSetting;
                if (profileBinding != null)
                    Object.DestroyImmediate(profileBinding);
                if (request?.DebugTimelineGroup?.ProfileInstance != null)
                    Object.DestroyImmediate(request.DebugTimelineGroup.ProfileInstance);
                if (independentRequest?.DebugTimelineGroup?.ProfileInstance != null)
                    Object.DestroyImmediate(independentRequest.DebugTimelineGroup.ProfileInstance);
                if (sourceBinding != null)
                    Object.DestroyImmediate(sourceBinding);
                if (directorObject != null)
                    Object.DestroyImmediate(directorObject);
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void TryPrepare_CreatesSeparateDebugBindingWithoutChangingUnboundFormalTrack()
        {
            KimodoPlayableClipGenerationSettings settings = KimodoPlayableClipGenerationSettings.instance;
            bool previousSetting = settings.WriteResampledTimelineCacheClips;
            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            GameObject directorObject = new GameObject("Unbound Debug Timeline Test Director");
            KimodoEditorGenerateRequest request = null;
            try
            {
                settings.WriteResampledTimelineCacheClips = true;
                Scene testScene = SceneManager.GetActiveScene();
                SceneManager.MoveGameObjectToScene(directorObject, testScene);
                PlayableDirector director = directorObject.AddComponent<PlayableDirector>();
                director.playableAsset = timeline;
                AnimationTrack sourceTrack = timeline.CreateTrack<AnimationTrack>(null, "Unbound Source");
                TimelineClip sourceTimelineClip = sourceTrack.CreateClip<KimodoPlayableClip>();
                KimodoPlayableClip sourceAsset = (KimodoPlayableClip)sourceTimelineClip.asset;
                request = new KimodoEditorGenerateRequest
                {
                    ModelName = KimodoMotionModelProfiles.DefaultModelName,
                    TargetFrameCount = 60,
                    TargetFrameRate = 30f,
                    TimelineClipSnapshot = sourceTimelineClip
                };

                KimodoDebugTimelineWriter.TryPrepare(sourceAsset, request, "Generation Pass");

                Assert.That(request.DebugTimelineReservation, Is.Not.Null);
                Assert.That(director.GetGenericBinding(sourceTrack), Is.Null);
                Assert.That(request.DebugTimelineGroup.Track, Is.Not.SameAs(sourceTrack));
                Assert.That(director.GetGenericBinding(request.DebugTimelineGroup.Track),
                    Is.SameAs(request.DebugTimelineGroup.Animator));
                Assert.That(request.DebugTimelineGroup.ProfileInstance.GetComponentsInChildren<Animator>(true)
                    .All(item => item.avatar == null), Is.True);
            }
            finally
            {
                settings.WriteResampledTimelineCacheClips = previousSetting;
                if (request?.DebugTimelineGroup?.ProfileInstance != null)
                    Object.DestroyImmediate(request.DebugTimelineGroup.ProfileInstance);
                Object.DestroyImmediate(directorObject);
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void TryPrepare_WhenDisabledDoesNotCreateProfileOrDebugTrack()
        {
            KimodoPlayableClipGenerationSettings settings = KimodoPlayableClipGenerationSettings.instance;
            bool previousSetting = settings.WriteResampledTimelineCacheClips;
            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            GameObject directorObject = new GameObject("Disabled Debug Timeline Test Director");
            try
            {
                settings.WriteResampledTimelineCacheClips = false;
                Scene testScene = SceneManager.GetActiveScene();
                SceneManager.MoveGameObjectToScene(directorObject, testScene);
                PlayableDirector director = directorObject.AddComponent<PlayableDirector>();
                director.playableAsset = timeline;
                AnimationTrack sourceTrack = timeline.CreateTrack<AnimationTrack>(null, "Source");
                TimelineClip sourceTimelineClip = sourceTrack.CreateClip<KimodoPlayableClip>();
                KimodoPlayableClip sourceAsset = (KimodoPlayableClip)sourceTimelineClip.asset;
                var request = new KimodoEditorGenerateRequest
                {
                    ModelName = KimodoMotionModelProfiles.DefaultModelName,
                    TargetFrameCount = 60,
                    TargetFrameRate = 30f,
                    TimelineClipSnapshot = sourceTimelineClip
                };
                int trackCount = timeline.GetOutputTracks().Count();

                KimodoDebugTimelineWriter.TryPrepare(sourceAsset, request, "Generation Pass");

                Assert.That(request.DebugTimelineGroup, Is.Null);
                Assert.That(request.DebugTimelineReservation, Is.Null);
                Assert.That(timeline.GetOutputTracks().Count(), Is.EqualTo(trackCount));
                Assert.That(testScene.GetRootGameObjects().Any(item =>
                    item.name.StartsWith("KimodoDebugProfile_", System.StringComparison.Ordinal)), Is.False);
            }
            finally
            {
                settings.WriteResampledTimelineCacheClips = previousSetting;
                Object.DestroyImmediate(directorObject);
                Object.DestroyImmediate(timeline);
            }
        }

        private static GameObject CreateProfileBinding(string modelName, Scene scene, out Animator animator)
        {
            string modelPath = AssetDatabase.GUIDToAssetPath(
                KimodoDebugTimelineWriter.ResolveDebugProfileModelGuid(modelName));
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            Assert.That(model, Is.Not.Null, $"Profile model asset was not found at '{modelPath}'.");
            GameObject instance = Object.Instantiate(model);
            instance.name = $"KimodoDebugProfile_Test_{modelName}";
            SceneManager.MoveGameObjectToScene(instance, scene);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (Animator profileAnimator in instance.GetComponentsInChildren<Animator>(true))
                profileAnimator.avatar = null;
            animator = instance.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null, "Profile model must contain an Animator for AnimationTrack binding.");
            return instance;
        }

        [Test]
        public void TryWriteConstraintPasses_CreatesOneRollClipPerPass()
        {
            KimodoPlayableClipGenerationSettings settings = KimodoPlayableClipGenerationSettings.instance;
            bool previousSetting = settings.WriteResampledTimelineCacheClips;
            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AnimationTrack track = timeline.CreateTrack<AnimationTrack>(null, "Source");
            TimelineClip sourceClip = track.CreateClip<KimodoPlayableClip>();
            GameObject directorObject = new GameObject("Debug Roll Test Director");
            KimodoEditorGenerateRequest request = null;
            Scene testScene = SceneManager.GetActiveScene();
            SceneManager.MoveGameObjectToScene(directorObject, testScene);
            PlayableDirector director = directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            try
            {
                settings.WriteResampledTimelineCacheClips = true;
                request = new KimodoEditorGenerateRequest
                {
                    ModelName = "SOMA30",
                    TargetFrameCount = 60,
                    TargetFrameRate = 30f,
                    TimelineClipSnapshot = sourceClip
                };
                KimodoDebugTimelineWriter.TryPrepare(
                    sourceClip.asset as KimodoPlayableClip,
                    request,
                    "Trajectory Pass 1");
                var pass = new KimodoDebugConstraintPass
                {
                    Name = "Trajectory Path Constraints",
                    AddedConstraintsJson = "[{\"type\":\"Root2D\",\"frame_indices\":[0,7,15,22,29],\"root_positions\":[[0,0],[1,0],[2,0],[3,0],[4,0]]}]",
                    CombinedConstraintsJson = "[{\"type\":\"Root2D\"},{\"type\":\"Effector\"}]",
                    ModelName = "SOMA30",
                    FrameRate = 30,
                    FrameCount = 60,
                    DebugTimelineStart = request.DebugTimelineReservation.TimelineClip.start
                };

                KimodoDebugTimelineWriter.TryWriteConstraintPasses(request, new[] { pass });

                KimodoDebugRollClip[] rollClips = request.DebugTimelineGroup.RollTrack.GetClips()
                    .Select(clip => clip.asset)
                    .OfType<KimodoDebugRollClip>()
                    .ToArray();
                Assert.That(rollClips, Has.Length.EqualTo(1));
                Assert.That(rollClips[0].PassName, Is.EqualTo(pass.Name));
                Assert.That(rollClips[0].AddedConstraintsJson, Does.Contain("Root2D"));
                Assert.That(rollClips[0].CombinedConstraintsJson, Does.Contain("Effector"));
                Assert.That(request.DebugTimelineGroup.RollTrack.GetClips().Single().start,
                    Is.EqualTo(pass.DebugTimelineStart).Within(1e-6));
                Assert.That(timeline.GetOutputTracks().Count(), Is.EqualTo(3));
            }
            finally
            {
                settings.WriteResampledTimelineCacheClips = previousSetting;
                if (request?.DebugTimelineGroup?.ProfileInstance != null)
                    Object.DestroyImmediate(request.DebugTimelineGroup.ProfileInstance);
                Object.DestroyImmediate(directorObject);
                Object.DestroyImmediate(timeline);
            }
        }

    }
}
