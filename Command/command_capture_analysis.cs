using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KimodoUnityBridge;
using KimodoBridge;
using TimelineInject;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KimodoUnityBridge.Command

{
    internal static partial class command_context
    {
        private const string PhaseTrackVersion = "1-temporal-cluster-v2-command-60";

        private static KimodoMarkerSampleResult[] CaptureCachedSampleResults(
            AnalysisCacheRecord record,
            TimelineCharacterRecord character,
            int startFrame,
            int frameCount)
        {
            string key = record?.Id ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(key) && AnalysisPoseSamples.TryGetValue(key, out KimodoMarkerSampleResult[] cached) &&
                cached != null && cached.Length >= frameCount)
            {
                return cached;
            }
            KimodoMarkerSampleResult[] samples = CaptureSampleResults(character, startFrame, frameCount);
            if (!string.IsNullOrWhiteSpace(key)) AnalysisPoseSamples[key] = samples;
            return samples;
        }

        // The analysis picture is the fixed 16:9 test-layout composite: four
        // overview tiles plus eight key poses and eight step poses.
        private static JObject RenderAnalysisPictures(
            TimelineSessionRecord session,
            IReadOnlyList<AnalysisSubject> subjects,
            JObject picture,
            int requestedResolution)
        {
            if (subjects == null || subjects.Count != 1)
            {
                throw new InvalidOperationException("Analysis picture rendering accepts exactly one clip.");
            }
            // All analysis picture requests now share capture.clip's real
            // SceneView camera and 20-tile layout. The former off-screen camera
            // renderers are archived under archive/analyze-picture-legacy; this command
            // path has no fallback to the removed off-screen renderer.
            bool includeScene = string.Equals(
                picture?["environment"]?["mode"]?.Value<string>(),
                "preserve",
                StringComparison.OrdinalIgnoreCase);
            JObject capture = RenderSceneAnalysisCapture(session, subjects[0], requestedResolution, includeScene);
            return (capture["pictures"] as JObject)?.DeepClone() as JObject ?? new JObject();
        }

        private static List<PictureTile> BuildTestAnalysisTiles(SubjectPictureData subject)
        {
            List<int> allKeyframes = GetAllAnalysisKeyframes(subject);
            List<int> allFootEvents = GetAllFootEventFrames(subject);
            IReadOnlyList<int?> keyframes = SelectUniformlySpacedKeyframes(subject, allKeyframes, 8);
            IReadOnlyList<int?> steps = SelectFirstNonOverlappingFrames(subject, allFootEvents, 8);
            var result = new List<PictureTile>
            {
                PictureTile.TestOverview(subject, "3d_track", Vector3.up),
                PictureTile.TestOverview(subject, "height_time_track", Vector3.up),
                PictureTile.TestSelectedOverview(subject, "3d_ghost", allKeyframes, new Vector3(1f, .75f, -1f)),
                PictureTile.TestSelectedOverview(subject, "3d_ghost_track", allFootEvents, new Vector3(1f, .75f, -1f))
            };
            for (int index = 0; index < 8; index++) result.Add(PictureTile.TestPoseSlot(subject, keyframes[index], "key_pose", index + 1));
            for (int index = 0; index < 8; index++) result.Add(PictureTile.TestPoseSlot(subject, steps[index], "step_pose", index + 1));
            result[2].Description["frames"] = new JArray(allKeyframes);
            result[3].Description["frames"] = new JArray(allFootEvents);
            result[2].Description["selected_frames"] = new JArray(keyframes.Where(value => value.HasValue).Select(value => value.Value));
            result[3].Description["selected_frames"] = new JArray(steps.Where(value => value.HasValue).Select(value => value.Value));
            result[2].Description["shared_with"] = "key_pose";
            result[3].Description["shared_with"] = "step_pose";
            return result;
        }

        private static List<int> GetAllAnalysisKeyframes(SubjectPictureData subject)
        {
            int last = Math.Max(0, subject.Pelvis.Length - 1);
            return (subject.Subject.Record.Analysis?["keyframes"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Select(item => Mathf.Clamp(
                    (item.Value<int?>("timeline_frame_60") ?? subject.Subject.StartFrame) - subject.Subject.StartFrame,
                    0,
                    last))
                .Distinct()
                .OrderBy(frame => frame)
                .ToList();
        }

        private static List<int> GetAllFootEventFrames(SubjectPictureData subject)
        {
            int last = Math.Max(0, subject.Pelvis.Length - 1);
            return (subject.Subject.Record.Analysis?["foot_contacts"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Where(IsFootLandingEvent)
                .Select(item => Mathf.Clamp(item.Value<int?>("frame") ?? 0, 0, last))
                .Distinct()
                .OrderBy(frame => frame)
                .ToList();
        }

        private static bool IsFootLandingEvent(JObject item)
        {
            return item?.Value<bool?>("contact") == true;
        }

        private static IReadOnlyList<int?> SelectFirstNonOverlappingFrames(
            SubjectPictureData subject,
            IEnumerable<int> candidates,
            int count)
        {
            List<int> validFrames = GetNonOverlappingFrames(subject, candidates);
            int targetCount = Math.Max(0, count);
            return validFrames.Take(targetCount).Select(frame => (int?)frame)
                .Concat(Enumerable.Repeat<int?>(null, Math.Max(0, targetCount - validFrames.Count)))
                .ToArray();
        }

        private static List<int> GetNonOverlappingFrames(
            SubjectPictureData subject,
            IEnumerable<int> candidates)
        {
            int last = Math.Max(0, subject.Pelvis.Length - 1);
            var selected = new List<int>();
            foreach (int frame in (candidates ?? Enumerable.Empty<int>())
                .Select(item => Mathf.Clamp(item, 0, last))
                .Distinct()
                .OrderBy(item => item))
            {
                Vector3 candidate = subject.Pelvis[frame];
                candidate.y = 0f;
                if (selected.Any(previousFrame =>
                {
                    Vector3 previous = subject.Pelvis[previousFrame];
                    previous.y = 0f;
                    return Vector3.Distance(previous, candidate) < .5f;
                }))
                {
                    continue;
                }

                selected.Add(frame);
            }
            return selected;
        }

        private static IReadOnlyList<int?> SelectUniformlySpacedKeyframes(
            SubjectPictureData subject,
            IEnumerable<int> candidates,
            int count)
        {
            int lastFrame = Math.Max(0, subject.Pelvis.Length - 1);
            int targetCount = Math.Max(0, count);
            if (targetCount == 0) return Array.Empty<int?>();

            // Keep the existing spatial de-duplication as the validity filter,
            // but evaluate every candidate before selecting evenly across the
            // resulting ordered set. Always make both clip endpoints eligible.
            List<int> validFrames = GetNonOverlappingFrames(
                subject,
                (candidates ?? Enumerable.Empty<int>()).Concat(new[] { 0, lastFrame }));
            if (!validFrames.Contains(0)) validFrames.Insert(0, 0);
            if (!validFrames.Contains(lastFrame)) validFrames.Add(lastFrame);
            validFrames = validFrames.Distinct().OrderBy(frame => frame).ToList();

            if (targetCount == 1) return new int?[] { 0 };

            int selectedCount = Math.Min(targetCount, validFrames.Count);
            var selected = new int?[selectedCount];
            for (int index = 0; index < selectedCount; index++)
            {
                if (index == 0)
                {
                    selected[index] = 0;
                    continue;
                }
                if (index == selectedCount - 1)
                {
                    selected[index] = lastFrame;
                    continue;
                }

                int candidateIndex = Mathf.RoundToInt(
                    index * (validFrames.Count - 1f) / (selectedCount - 1f));
                selected[index] = validFrames[Mathf.Clamp(candidateIndex, 0, validFrames.Count - 1)];
            }
            return selected.Concat(Enumerable.Repeat<int?>(null, targetCount - selectedCount)).ToArray();
        }

        private static IReadOnlyList<int?> NormalizeTestKeyframes(SubjectPictureData subject, int count)
        {
            return SelectUniformlySpacedKeyframes(subject, GetAllAnalysisKeyframes(subject), count);
        }

        private static IReadOnlyList<int?> NormalizeTestStepFrames(SubjectPictureData subject, int count)
        {
            return SelectFirstNonOverlappingFrames(subject, GetAllFootEventFrames(subject), count);
        }

        private static Texture2D CreateEmptyTestTile(int width, int height)
        {
            var texture = new Texture2D(Math.Max(1, width), Math.Max(1, height), TextureFormat.RGBA32, false);
            Fill(texture, new Color(.04f, .05f, .07f, 1f));
            texture.Apply(false, false);
            return texture;
        }

        private static Bounds CalculateTestContentBounds(SubjectPictureData subject)
        {
            if (subject == null || subject.Pelvis == null || subject.Pelvis.Length == 0)
            {
                return new Bounds(Vector3.up, Vector3.one);
            }

            Bounds bounds = new Bounds(subject.Pelvis[0], Vector3.zero);
            // The viewport follows the sampled trajectory markers directly.
            // Do not extrapolate a body envelope from first/last mesh bounds;
            // that mixes a world-space mesh box with every Hips sample.
            foreach (Vector3 point in subject.Pelvis) bounds.Encapsulate(point);
            foreach (Vector3 point in subject.LeftHand) bounds.Encapsulate(point);
            foreach (Vector3 point in subject.RightHand) bounds.Encapsulate(point);
            foreach (Vector3 point in subject.LeftFoot) bounds.Encapsulate(point);
            foreach (Vector3 point in subject.RightFoot) bounds.Encapsulate(point);
            foreach (Vector3 point in subject.Head) bounds.Encapsulate(point);

            return bounds;
        }

        private static void CalculateTestViewExtents(
            SubjectPictureData subject,
            Vector3 direction,
            out Vector3 viewCenter,
            out float maxHorizontal,
            out float maxVertical,
            out float maxDepth)
        {
            CalculateTestViewExtents(
                subject.Pelvis.Concat(subject.LeftHand)
                    .Concat(subject.RightHand).Concat(subject.LeftFoot).Concat(subject.RightFoot)
                    .Concat(subject.Head),
                direction,
                out viewCenter,
                out maxHorizontal,
                out maxVertical,
                out maxDepth);
        }

        private static void CalculateTestViewExtents(
            Bounds bounds,
            Vector3 direction,
            out Vector3 viewCenter,
            out float maxHorizontal,
            out float maxVertical,
            out float maxDepth)
        {
            CalculateTestViewExtents(
                new[]
                {
                    new Vector3(bounds.min.x, bounds.min.y, bounds.min.z),
                    new Vector3(bounds.min.x, bounds.min.y, bounds.max.z),
                    new Vector3(bounds.min.x, bounds.max.y, bounds.min.z),
                    new Vector3(bounds.min.x, bounds.max.y, bounds.max.z),
                    new Vector3(bounds.max.x, bounds.min.y, bounds.min.z),
                    new Vector3(bounds.max.x, bounds.min.y, bounds.max.z),
                    new Vector3(bounds.max.x, bounds.max.y, bounds.min.z),
                    new Vector3(bounds.max.x, bounds.max.y, bounds.max.z)
                },
                direction,
                out viewCenter,
                out maxHorizontal,
                out maxVertical,
                out maxDepth);
        }

        private static void CalculateTestViewExtents(
            IEnumerable<Vector3> points,
            Vector3 direction,
            out Vector3 viewCenter,
            out float maxHorizontal,
            out float maxVertical,
            out float maxDepth)
        {
            Vector3 normalizedDirection = direction.sqrMagnitude > .0001f
                ? direction.normalized
                : new Vector3(1f, .75f, -1f).normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(normalizedDirection, Vector3.up)) > .95f
                ? Vector3.forward
                : Vector3.up;
            Quaternion inverseView = Quaternion.Inverse(Quaternion.LookRotation(-normalizedDirection, up));
            float minHorizontal = float.PositiveInfinity;
            float maxHorizontalValue = float.NegativeInfinity;
            float minVertical = float.PositiveInfinity;
            float maxVerticalValue = float.NegativeInfinity;
            float minDepth = float.PositiveInfinity;
            float maxDepthValue = float.NegativeInfinity;
            foreach (Vector3 point in points)
            {
                Vector3 local = inverseView * point;
                minHorizontal = Mathf.Min(minHorizontal, local.x);
                maxHorizontalValue = Mathf.Max(maxHorizontalValue, local.x);
                minVertical = Mathf.Min(minVertical, local.y);
                maxVerticalValue = Mathf.Max(maxVerticalValue, local.y);
                minDepth = Mathf.Min(minDepth, local.z);
                maxDepthValue = Mathf.Max(maxDepthValue, local.z);
            }

            if (float.IsPositiveInfinity(minHorizontal))
            {
                viewCenter = Vector3.zero;
                maxHorizontal = maxVertical = maxDepth = 0f;
                return;
            }

            Vector3 localCenter = new Vector3(
                (minHorizontal + maxHorizontalValue) * .5f,
                (minVertical + maxVerticalValue) * .5f,
                (minDepth + maxDepthValue) * .5f);
            viewCenter = Quaternion.Inverse(inverseView) * localCenter;
            maxHorizontal = (maxHorizontalValue - minHorizontal) * .5f;
            maxVertical = (maxVerticalValue - minVertical) * .5f;
            maxDepth = (maxDepthValue - minDepth) * .5f;
        }

        private static Vector3[] ExpandPosePointsAwayFromHipsInCameraSpace(
            IReadOnlyList<Vector3> points,
            Vector3 direction,
            Vector3 characterForward)
        {
            if (points == null || points.Count == 0) return Array.Empty<Vector3>();
            Vector3 normalizedDirection = direction.sqrMagnitude > .0001f
                ? direction.normalized
                : new Vector3(1f, .75f, -1f).normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(normalizedDirection, Vector3.up)) > .95f
                ? Vector3.forward
                : Vector3.up;
            Quaternion toCameraSpace = Quaternion.Inverse(Quaternion.LookRotation(-normalizedDirection, up));
            Quaternion toWorldSpace = Quaternion.Inverse(toCameraSpace);
            Vector3 hips = toCameraSpace * points[0];
            Vector3 forward = toCameraSpace * characterForward;
            var expanded = new List<Vector3>(points.Count * 2 + 2);
            expanded.AddRange(points);
            for (int index = 1; index < points.Count; index++)
            {
                Vector3 joint = toCameraSpace * points[index];
                Vector3 fromHips = joint - hips;
                if (fromHips.sqrMagnitude > .0001f)
                {
                    float offset = index == points.Count - 1 ? TestPoseHeadCameraOffsetMeters : TestPoseJointCameraOffsetMeters;
                    joint += fromHips.normalized * offset;
                }
                expanded.Add(toWorldSpace * joint);
                if ((index == 5 || index == 6) && forward.sqrMagnitude > .0001f)
                {
                    expanded.Add(toWorldSpace * (joint + forward.normalized * TestPoseFootForwardCameraOffsetMeters));
                }
            }
            return expanded.ToArray();
        }

        private static SubjectPictureData BuildSubjectPictureData(TimelineSessionRecord session, AnalysisSubject subject)
        {
            if (!IsHumanoidCharacter(subject.Character))
            {
                return BuildMeshSubjectPictureData(subject);
            }

            int frameCount = Math.Max(1, subject.EndFrameExclusive - subject.StartFrame);
            var pelvis = new Vector3[frameCount];
            var leftHand = new Vector3[frameCount];
            var rightHand = new Vector3[frameCount];
            var leftFoot = new Vector3[frameCount];
            var rightFoot = new Vector3[frameCount];
            var leftElbow = new Vector3[frameCount];
            var rightElbow = new Vector3[frameCount];
            var leftKnee = new Vector3[frameCount];
            var rightKnee = new Vector3[frameCount];
            var head = new Vector3[frameCount];
            var poseBounds = new Bounds[frameCount];
            Bounds firstBounds = default;
            Bounds lastBounds = default;
            Bounds allPoseBounds = default;
            bool hasPoseBounds = false;
            double originalTime = session.Director.time;
            EvaluatedPosePreview posePreview = null;
            KimodoMarkerSampleResult[] samples;
            try
            {
                samples = CaptureCachedSampleResults(subject.Record, subject.Character, subject.StartFrame, frameCount);
                posePreview = CreatePipelinePosePreview(subject.Character, samples[0]);
                Animator poseAnimator = posePreview.Animator
                    ?? throw new InvalidOperationException($"Character '{subject.Character.Name}' pose preview has no Animator.");
                for (int localFrame = 0; localFrame < frameCount; localFrame++)
                {
                    KimodoMarkerSampleResult sample = samples[localFrame];
                    posePreview.Apply(sample);

                    Transform hips = poseAnimator.GetBoneTransform(HumanBodyBones.Hips);
                    if (hips == null)
                    {
                        throw new InvalidOperationException($"Character '{subject.Character.Name}' has no Humanoid Hips transform.");
                    }
                    pelvis[localFrame] = hips.position;
                    leftHand[localFrame] = ReadHumanoidBonePosition(poseAnimator, HumanBodyBones.LeftHand, subject.Character.Name);
                    rightHand[localFrame] = ReadHumanoidBonePosition(poseAnimator, HumanBodyBones.RightHand, subject.Character.Name);
                    leftFoot[localFrame] = ReadHumanoidBonePosition(poseAnimator, HumanBodyBones.LeftFoot, subject.Character.Name);
                    rightFoot[localFrame] = ReadHumanoidBonePosition(poseAnimator, HumanBodyBones.RightFoot, subject.Character.Name);
                    leftElbow[localFrame] = ReadHumanoidBonePosition(poseAnimator, HumanBodyBones.LeftLowerArm, subject.Character.Name);
                    rightElbow[localFrame] = ReadHumanoidBonePosition(poseAnimator, HumanBodyBones.RightLowerArm, subject.Character.Name);
                    leftKnee[localFrame] = ReadHumanoidBonePosition(poseAnimator, HumanBodyBones.LeftLowerLeg, subject.Character.Name);
                    rightKnee[localFrame] = ReadHumanoidBonePosition(poseAnimator, HumanBodyBones.RightLowerLeg, subject.Character.Name);
                    head[localFrame] = ReadHumanoidBonePosition(poseAnimator, HumanBodyBones.Head, subject.Character.Name);
                    Bounds currentBounds = CalculateSkinnedBounds(posePreview.Root);
                    poseBounds[localFrame] = currentBounds;
                    if (localFrame == 0) firstBounds = currentBounds;
                    if (localFrame == frameCount - 1) lastBounds = currentBounds;
                    if (!hasPoseBounds)
                    {
                        allPoseBounds = currentBounds;
                        hasPoseBounds = true;
                    }
                    else
                    {
                        allPoseBounds.Encapsulate(currentBounds);
                    }
                }
            }
            finally
            {
                posePreview?.Dispose();
                session.Director.time = originalTime;
                session.Director.Evaluate();
            }

            bool[] leftContacts = new bool[frameCount];
            bool[] rightContacts = new bool[frameCount];
            string motionPath = ProjectRelativePathToAbsolute(subject.Record.MotionPath);
            if (File.Exists(motionPath) && KimodoRawMotionUtility.TryParseFlatBuffer(File.ReadAllBytes(motionPath), out KimodoRawMotionData motion, out _))
            {
                int count = Math.Min(frameCount, motion.FrameCount);
                for (int frame = 0; frame < count; frame++)
                {
                    motion.TryReadFootContact(frame, 0, out float leftHeel);
                    motion.TryReadFootContact(frame, 1, out float leftToe);
                    motion.TryReadFootContact(frame, 2, out float rightHeel);
                    motion.TryReadFootContact(frame, 3, out float rightToe);
                    leftContacts[frame] = leftHeel >= .5f || leftToe >= .5f;
                    rightContacts[frame] = rightHeel >= .5f || rightToe >= .5f;
                }
            }

            // Keep the legacy bounds for non-test mesh evidence. The complete
            // pose bounds are exposed separately for the test renderer.
            Bounds bounds = firstBounds;
            foreach (Vector3 point in pelvis) bounds.Encapsulate(point);
            bounds.Encapsulate(lastBounds);
            bounds.Expand(new Vector3(6f, 1f, 6f));
            if (bounds.size.x < 6f) bounds.Expand(new Vector3(6f - bounds.size.x, 0f, 0f));
            if (bounds.size.z < 6f) bounds.Expand(new Vector3(0f, 0f, 6f - bounds.size.z));
            Bounds testBounds = hasPoseBounds ? allPoseBounds : bounds;
            foreach (Vector3 point in pelvis) testBounds.Encapsulate(point);
            return new SubjectPictureData(
                subject,
                samples,
                pelvis,
                leftHand,
                rightHand,
                leftFoot,
                rightFoot,
                leftElbow,
                rightElbow,
                leftKnee,
                rightKnee,
                head,
                leftContacts,
                rightContacts,
                poseBounds,
                firstBounds,
                lastBounds,
                bounds,
                testBounds);
        }

        private static SubjectPictureData BuildMeshSubjectPictureData(AnalysisSubject subject)
        {
            int frameCount = Math.Max(1, subject.EndFrameExclusive - subject.StartFrame);
            var positions = new Vector3[frameCount];
            var poseBounds = new Bounds[frameCount];
            Bounds firstBounds = default;
            Bounds lastBounds = default;
            Bounds allBounds = default;
            bool hasBounds = false;
            for (int localFrame = 0; localFrame < frameCount; localFrame++)
            {
                GameObject preview = CreateMeshPosePreview(subject, localFrame);
                try
                {
                    positions[localFrame] = preview.transform.position;
                    Bounds current = CalculateSkinnedBounds(preview);
                    poseBounds[localFrame] = current;
                    if (localFrame == 0) firstBounds = current;
                    if (localFrame == frameCount - 1) lastBounds = current;
                    if (!hasBounds)
                    {
                        allBounds = current;
                        hasBounds = true;
                    }
                    else
                    {
                        allBounds.Encapsulate(current);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(preview);
                }
            }

            Bounds bounds = hasBounds ? allBounds : CalculateBounds(subject.Character.Root);
            bounds.Expand(new Vector3(1f, .5f, 1f));
            return new SubjectPictureData(
                subject,
                null,
                positions,
                positions,
                positions,
                positions,
                positions,
                positions,
                positions,
                positions,
                positions,
                positions,
                new bool[frameCount],
                new bool[frameCount],
                poseBounds,
                firstBounds,
                lastBounds,
                bounds,
                bounds);
        }

        private static Vector3 ReadHumanoidBonePosition(Animator animator, HumanBodyBones bone, string characterName)
        {
            if (animator == null)
            {
                throw new InvalidOperationException($"Character '{characterName}' preview has no Animator while reading {bone}.");
            }
            Transform transform = animator.GetBoneTransform(bone);
            if (transform == null)
            {
                throw new InvalidOperationException($"Character '{characterName}' has no Humanoid {bone} transform.");
            }
            return transform.position;
        }

        private static Bounds CalculateSkinnedBounds(GameObject root)
        {
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0) return CalculateBounds(root);
            Bounds result = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) result.Encapsulate(renderers[index].bounds);
            return result;
        }

        private static List<PictureTile> BuildPictureTiles(SubjectPictureData subject, AnalysisPictureRequest request)
        {
            if (!IsHumanoidCharacter(subject.Subject.Character))
            {
                return SelectKeyFrames(subject, subject.KeyframeCount)
                    .Select(frame => PictureTile.MeshPose(subject, frame, "mesh_pose"))
                    .ToList();
            }

            var result = new List<PictureTile>();
            if (request.Includes("3d_track")) result.Add(PictureTile.TestOverview(subject, "3d_track", Vector3.up));
            if (request.Includes("height_time_track")) result.Add(PictureTile.TestOverview(subject, "height_time_track", Vector3.up));
            List<int> allKeyframes = GetAllAnalysisKeyframes(subject);
            List<int> allFootEvents = GetAllFootEventFrames(subject);
            IReadOnlyList<int?> keyframes = SelectUniformlySpacedKeyframes(subject, allKeyframes, 8);
            IReadOnlyList<int?> steps = SelectFirstNonOverlappingFrames(subject, allFootEvents, 8);
            if (request.Includes("3d_ghost"))
            {
                result.Add(PictureTile.TestSelectedOverview(subject, "3d_ghost", allKeyframes, new Vector3(1f, .75f, -1f)));
            }
            if (request.Includes("3d_ghost_track"))
            {
                result.Add(PictureTile.TestSelectedOverview(subject, "3d_ghost_track", allFootEvents, new Vector3(1f, .75f, -1f)));
            }
            if (request.Includes("key_pose"))
            {
                for (int index = 0; index < keyframes.Count; index++) result.Add(PictureTile.TestPoseSlot(subject, keyframes[index], "key_pose", index + 1));
            }
            if (request.Includes("step_pose"))
            {
                for (int index = 0; index < steps.Count; index++) result.Add(PictureTile.TestPoseSlot(subject, steps[index], "step_pose", index + 1));
            }
            return result;
        }

        private static List<int> SelectKeyFrames(SubjectPictureData subject, int count)
        {
            int lastFrame = Math.Max(0, subject.Pelvis.Length - 1);
            return (subject.Subject.Record.Analysis?["keyframes"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Select(item => Mathf.Clamp(
                    (item.Value<int?>("timeline_frame_60") ?? subject.Subject.StartFrame) - subject.Subject.StartFrame,
                    0,
                    lastFrame))
                .Distinct()
                .OrderBy(frame => frame)
                .ToList();
        }

        private sealed class PhaseSegment
        {
            public int StartFrame;
            public int EndFrame;
            public int AnchorFrame;
            public float MeanChange;
            public float UpperBodyActivity;
            public float RootSpeed;
            public int FootEventCount;
            public bool Transition;
        }

        private sealed class PhaseFeature
        {
            public Vector3[] Points;
            public float Speed;
            public float UpperBodySpeed;
            public bool LeftContact;
            public bool RightContact;
        }

        private static void EnsurePhaseTrack(SubjectPictureData subject)
        {
            if (subject == null || subject.Pelvis == null || subject.Pelvis.Length == 0 ||
                !IsHumanoidCharacter(subject.Subject.Character)) return;

            JArray phases = BuildPhaseTrack(subject);
            JObject analysis = subject.Subject.Record.Analysis ?? new JObject();
            analysis["phase_track_version"] = PhaseTrackVersion;
            analysis["phase_track"] = phases;
            if (analysis["motion_profile"] is JObject profile &&
                subject.Subject.Record.RootTrajectory?["samples"] is JArray trajectorySamples)
            {
                var headings = phases.OfType<JObject>()
                    .Select(phase => phase.Value<int?>("anchor_frame") ?? 0)
                    .Select(frame => trajectorySamples.OfType<JObject>().FirstOrDefault(sample =>
                        sample.Value<int?>("frame") == frame)?["heading_xz"] as JArray)
                    .Where(value => value != null && value.Count >= 2)
                    .Select(value => Mathf.Atan2(value[0].Value<float>(), value[1].Value<float>()) * Mathf.Rad2Deg)
                    .ToArray();
                float firstHeading = headings.Length > 0 ? headings[0] : 0f;
                bool consistent = headings.Length > 0 && headings.All(value => Mathf.Abs(Mathf.DeltaAngle(firstHeading, value)) <= 8f);
                profile["phase_anchor_heading_consistent"] = consistent;
                profile["phase_anchor_heading_sample_count"] = headings.Length;
            }
            analysis["keyframes"] = new JArray(phases.OfType<JObject>().Select(item => new JObject
            {
                ["timeline_frame_60"] = subject.Subject.StartFrame + item.Value<int>("anchor_frame"),
                ["phase_index"] = item.Value<int>("phase_index"),
                ["kind"] = item.Value<string>("kind")
            }));
            subject.Subject.Record.Analysis = analysis;
            subject.RefreshKeyframes();
        }

        private static JArray BuildPhaseTrack(SubjectPictureData subject)
        {
            int frameCount = subject.Pelvis.Length;
            PhaseFeature[] features = Enumerable.Range(0, frameCount)
                .Select(frame => BuildPhaseFeature(subject, frame)).ToArray();
            var changes = new float[Math.Max(0, frameCount - 1)];
            for (int frame = 1; frame < frameCount; frame++) changes[frame - 1] =
                PhaseFeatureDistance(features[frame - 1], features[frame]);
            float threshold = Mathf.Clamp(Mathf.Max(.06f, Median(changes) * 3f), .08f, .8f);
            int minimumFrames = Mathf.Clamp(Mathf.RoundToInt((float)SessionFrameRate * .2f), 6, 18);
            var segments = new List<PhaseSegment>();
            int start = 0;
            for (int frame = 1; frame < frameCount; frame++)
            {
                bool contactChanged = features[frame].LeftContact != features[frame - 1].LeftContact ||
                    features[frame].RightContact != features[frame - 1].RightContact;
                bool boundary = frame - start >= minimumFrames &&
                    (changes[frame - 1] > threshold || (contactChanged && changes[frame - 1] > threshold * .65f));
                if (!boundary) continue;
                segments.Add(CreatePhaseSegment(features, changes, start, frame - 1, threshold));
                start = frame;
            }
            segments.Add(CreatePhaseSegment(features, changes, start, frameCount - 1, threshold));
            MergeShortPhaseSegments(features, changes, segments, minimumFrames, threshold);
            for (int index = 0; index < segments.Count; index++)
            {
                if (segments[index].StartFrame != (index == 0 ? 0 : segments[index - 1].EndFrame + 1) ||
                    segments[index].EndFrame < segments[index].StartFrame ||
                    segments[index].AnchorFrame < segments[index].StartFrame ||
                    segments[index].AnchorFrame > segments[index].EndFrame)
                {
                    throw new InvalidOperationException("Phase-track clustering produced non-contiguous intervals.");
                }
            }
            if (segments[segments.Count - 1].EndFrame != frameCount - 1)
            {
                throw new InvalidOperationException("Phase-track clustering did not cover the complete clip.");
            }

            var result = new JArray();
            for (int index = 0; index < segments.Count; index++)
            {
                PhaseSegment segment = segments[index];
                int duration = segment.EndFrame - segment.StartFrame + 1;
                result.Add(new JObject
                {
                    ["phase_index"] = index,
                    ["start_frame"] = segment.StartFrame,
                    ["end_frame"] = segment.EndFrame,
                    ["start_frame_60"] = subject.Subject.StartFrame + segment.StartFrame,
                    ["end_frame_60"] = subject.Subject.StartFrame + segment.EndFrame,
                    ["duration_frames"] = duration,
                    ["duration_seconds"] = duration / SessionFrameRate,
                    ["anchor_frame"] = segment.AnchorFrame,
                    ["anchor_frame_60"] = subject.Subject.StartFrame + segment.AnchorFrame,
                    ["anchor_time_seconds"] = (subject.Subject.StartFrame + segment.AnchorFrame) / SessionFrameRate,
                    ["kind"] = segment.Transition ? "transition" : "phase",
                    ["confidence"] = segment.MeanChange <= threshold ? "high" : "medium",
                    ["mean_feature_change"] = segment.MeanChange,
                    ["mean_root_speed"] = segment.RootSpeed,
                    ["upper_body_activity"] = segment.UpperBodyActivity,
                    ["foot_event_count"] = segment.FootEventCount
                });
            }
            return result;
        }

        private static PhaseFeature BuildPhaseFeature(SubjectPictureData subject, int frame)
        {
            int previous = Mathf.Max(0, frame - 1);
            Vector3 pelvis = subject.Pelvis[frame];
            return new PhaseFeature
            {
                Points = new[]
                {
                    subject.LeftHand[frame] - pelvis,
                    subject.RightHand[frame] - pelvis,
                    subject.LeftElbow[frame] - pelvis,
                    subject.RightElbow[frame] - pelvis,
                    subject.LeftFoot[frame] - pelvis,
                    subject.RightFoot[frame] - pelvis,
                    subject.Head[frame] - pelvis
                },
                Speed = Vector3.Distance(subject.Pelvis[frame], subject.Pelvis[previous]) * (float)SessionFrameRate,
                UpperBodySpeed = (
                    Vector3.Distance(subject.LeftHand[frame], subject.LeftHand[previous]) +
                    Vector3.Distance(subject.RightHand[frame], subject.RightHand[previous]) +
                    Vector3.Distance(subject.LeftElbow[frame], subject.LeftElbow[previous]) +
                    Vector3.Distance(subject.RightElbow[frame], subject.RightElbow[previous])) *
                    (float)SessionFrameRate,
                LeftContact = subject.LeftContacts[frame],
                RightContact = subject.RightContacts[frame]
            };
        }

        private static float PhaseFeatureDistance(PhaseFeature first, PhaseFeature second)
        {
            float distance = 0f;
            for (int index = 0; index < first.Points.Length; index++)
                distance += Vector3.Distance(first.Points[index], second.Points[index]);
            distance += Mathf.Abs(first.Speed - second.Speed) * .05f;
            if (first.LeftContact != second.LeftContact) distance += .6f;
            if (first.RightContact != second.RightContact) distance += .6f;
            return distance / (first.Points.Length + 1f);
        }

        private static PhaseSegment CreatePhaseSegment(
            PhaseFeature[] features,
            float[] changes,
            int start,
            int end,
            float threshold)
        {
            int anchor = start;
            float best = float.PositiveInfinity;
            float meanChange = 0f;
            float rootSpeed = 0f;
            float upperBodyActivity = 0f;
            int footEvents = 0;
            int changeCount = 0;
            for (int frame = start; frame <= end; frame++)
            {
                rootSpeed += features[frame].Speed;
                upperBodyActivity += features[frame].UpperBodySpeed;
                if (frame > start &&
                    (features[frame].LeftContact != features[frame - 1].LeftContact ||
                     features[frame].RightContact != features[frame - 1].RightContact)) footEvents++;
                float distance = 0f;
                if (frame > start) { distance += changes[frame - 1]; meanChange += changes[frame - 1]; changeCount++; }
                if (frame < end) distance += changes[frame];
                if (distance < best) { best = distance; anchor = frame; }
            }
            meanChange /= Mathf.Max(1, changeCount);
            return new PhaseSegment
            {
                StartFrame = start,
                EndFrame = end,
                AnchorFrame = anchor,
                MeanChange = meanChange,
                RootSpeed = rootSpeed / Mathf.Max(1, end - start + 1),
                UpperBodyActivity = upperBodyActivity / Mathf.Max(1, end - start + 1),
                FootEventCount = footEvents,
                Transition = meanChange > threshold * 1.25f
            };
        }

        private static void MergeShortPhaseSegments(
            PhaseFeature[] features,
            float[] changes,
            List<PhaseSegment> segments,
            int minimumFrames,
            float threshold)
        {
            for (int index = 0; index < segments.Count && segments.Count > 1; index++)
            {
                PhaseSegment segment = segments[index];
                if (segment.EndFrame - segment.StartFrame + 1 >= minimumFrames) continue;
                int neighbor = index == 0 ? 1 : index - 1;
                int mergedStart = Mathf.Min(segments[neighbor].StartFrame, segment.StartFrame);
                int mergedEnd = Mathf.Max(segments[neighbor].EndFrame, segment.EndFrame);
                PhaseSegment merged = CreatePhaseSegment(features, changes, mergedStart, mergedEnd, threshold);
                int insertAt = Mathf.Min(index, neighbor);
                segments.RemoveAt(Mathf.Max(index, neighbor));
                segments.RemoveAt(Mathf.Min(index, neighbor));
                segments.Insert(insertAt, merged);
                index = Mathf.Max(-1, index - 2);
            }
        }

        private static float Median(float[] values)
        {
            if (values == null || values.Length == 0) return 0f;
            float[] ordered = values.OrderBy(value => value).ToArray();
            int middle = ordered.Length / 2;
            return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) * .5f : ordered[middle];
        }

    }
}
