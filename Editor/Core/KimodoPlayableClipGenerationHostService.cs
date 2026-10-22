using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using TimelineInject;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoBridge.Editor
{
    internal static class KimodoPlayableClipGenerationHostService
    {
        private const string ReplaceTimelineAnimationUndoName = "Kimodo Replace Timeline Animation";
        private static readonly KimodoEditorConstraintProvider ConstraintProvider = new KimodoEditorConstraintProvider();
        public static KimodoEditorGenerateRequest BuildRequest(
            KimodoPlayableClip clip,
            string prompt,
            KimodoExternalConstraintRequest externalConstraint,
            CancellationToken token,
            int? effectiveSeedOverride = null,
            bool disableTimelineInOut = false,
            bool deferConstraintNormalization = false,
            bool enableAutoBeginAnchor = true,
            TimelineClip timelineClipOverride = null,
            bool? generateLoopOverride = null,
            bool deferOutConstraint = false,
            bool deferInConstraint = false)
        {
            if (clip == null)
            {
                throw new InvalidOperationException("Playable clip is null.");
            }

            string resolvedModelName = KimodoMotionModelProfiles.NormalizeName(clip.bridgeModelName);
            bool isArdy = KimodoMotionModelProfiles.TryGetArdy(
                resolvedModelName,
                out KimodoMotionModelProfile ardyProfile);
            TimelineClip timelineClip = timelineClipOverride ?? KimodoTimelineClipResolver.FindTimelineClipForAsset(clip);
            if (timelineClip == null || timelineClip.duration <= 0.0)
            {
                throw new InvalidOperationException("Generation length requires a Timeline clip with positive duration.");
            }
            float targetFrameRate = KimodoMotionModelProfiles.ResolveGenerationFrameRate(resolvedModelName);
            int targetFrameCount = Mathf.Max(
                KimodoMotionModelProfiles.MinGenerationFrames,
                KimodoFrameTimeUtility.SecondsToFrameCount(timelineClip.duration, targetFrameRate));
            int generationFrameCount = Mathf.Max(
                targetFrameCount,
                KimodoFrameTimeUtility.SecondsToFrameCount(
                    KimodoGenerationWarningCheckService.MinimumGenerationDurationSeconds,
                    targetFrameRate));
            int sourceSessionFrameCount = Mathf.Max(1, Mathf.RoundToInt((float)(timelineClip.duration * 60.0)));
            bool isLoopGeneration = (generateLoopOverride ?? clip.generateLoop) && sourceSessionFrameCount <= 300;
            int loopPaddingFrames = isLoopGeneration ? generationFrameCount / 2 : 0;
            bool useOutsideGuardFrame = ShouldUseOutsideGuardFrame(
                clip,
                externalConstraint,
                disableTimelineInOut);
            int contextBefore = useOutsideGuardFrame ? 1 : 0;
            int contextAfter = 0;
            if (!isArdy && !disableTimelineInOut &&
                (externalConstraint?.Enabled != true || externalConstraint.IncludeTimelineConstraints))
            {
                KimodoInOutConstraintTools.ResolveOutsideContextFrames(timelineClip, out contextBefore, out contextAfter);
            }
            if (externalConstraint?.Enabled == true)
            {
                if (externalConstraint.ContextBeforeFrames < 0 || externalConstraint.ContextAfterFrames < 0)
                    throw new InvalidOperationException("External context frame counts must be non-negative.");
                contextBefore = Math.Max(contextBefore, externalConstraint.ContextBeforeFrames);
                contextAfter = Math.Max(contextAfter, externalConstraint.ContextAfterFrames);
            }
            int runtimeFrameCount = (isLoopGeneration ? generationFrameCount * 2 : generationFrameCount) +
                contextBefore + contextAfter;
            int runtimeTrimStartFrame = loopPaddingFrames + contextBefore;
            double runtimeSampleOffsetSeconds = runtimeTrimStartFrame / (double)targetFrameRate;
            float runtimeLengthSeconds = runtimeFrameCount / targetFrameRate;

            KimodoInOutConstraintAdapter.TryResolveTimelineContext(
                timelineClip,
                out KimodoTimelineInOutConstraintContext timelineContextSnapshot,
                out _);
            if (timelineContextSnapshot != null)
            {
                KimodoTimelineTrackOffsetUtility.CaptureWorldOffset(
                    timelineContextSnapshot.Track,
                    timelineContextSnapshot.Animator,
                    out timelineContextSnapshot.TrackOffsetPosition,
                    out timelineContextSnapshot.TrackOffsetRotation,
                    out _);
                timelineContextSnapshot.HasTrackOffsetSnapshot = true;
            }

            KimodoInOutConstraintResult constraintResult =
                ConstraintProvider.BuildGenerationConstraintsOrThrow(
                    clip,
                    externalConstraint,
                    runtimeFrameCount,
                    runtimeLengthSeconds,
                    targetFrameRate,
                    disableTimelineInOut,
                    deferConstraintNormalization,
                    enableAutoBeginAnchor,
                    runtimeSampleOffsetSeconds,
                    timelineClip,
                    deferOutConstraint,
                    deferInConstraint);
            string constraintsJson = constraintResult.ConstraintsJson;
            string deferredInConstraintJson = constraintResult.DeferredInConstraintJson;
            string deferredOutConstraintJson = constraintResult.DeferredOutConstraintJson;
            if (deferInConstraint)
            {
                KimodoInOutConstraintResult deferredIn =
                    ConstraintProvider.BuildDeferredInConstraintOrThrow(
                        clip,
                        runtimeFrameCount,
                        disableTimelineInOut,
                        runtimeSampleOffsetSeconds,
                        timelineClip);
                if (!string.IsNullOrWhiteSpace(deferredIn.ConstraintsJson))
                {
                    deferredInConstraintJson = CombineConstraintJson(
                        deferredInConstraintJson,
                        deferredIn.ConstraintsJson);
                }
            }
            if (deferOutConstraint)
            {
                KimodoInOutConstraintResult deferredOut =
                    ConstraintProvider.BuildDeferredOutConstraintOrThrow(
                        clip,
                        runtimeFrameCount,
                        disableTimelineInOut,
                        runtimeSampleOffsetSeconds,
                        timelineClip);
                if (!string.IsNullOrWhiteSpace(deferredOut.ConstraintsJson))
                {
                    deferredOutConstraintJson = CombineConstraintJson(
                        deferredOutConstraintJson,
                        deferredOut.ConstraintsJson);
                }
            }
            List<KimodoMarkerSampleResult> constraintSamples = constraintResult.CombinedSamples;
            if ((deferInConstraint || deferOutConstraint) && externalConstraint?.Enabled == true &&
                ((deferInConstraint && externalConstraint.DeferredInConstraintSamples?.Count > 0) ||
                 (deferOutConstraint && externalConstraint.DeferredOutConstraintSamples?.Count > 0)))
            {
                constraintSamples = new List<KimodoMarkerSampleResult>(constraintSamples);
                if (deferInConstraint)
                    KimodoInOutConstraintComposer.AppendSamples(externalConstraint.DeferredInConstraintSamples, constraintSamples);
                if (deferOutConstraint)
                    KimodoInOutConstraintComposer.AppendSamples(externalConstraint.DeferredOutConstraintSamples, constraintSamples);
                int immediateCount = constraintResult.CombinedSamples.Count;
                for (int i = immediateCount; i < constraintSamples.Count; i++)
                {
                    constraintSamples[i].sampleTime += runtimeSampleOffsetSeconds;
                }
            }
            bool hasSyntheticAutoBeginConstraint = constraintResult.HasSyntheticAutoBeginConstraint;

            ArdyEditorHistorySource initialHistorySource = null;
            if (isArdy)
            {
                if (!disableTimelineInOut)
                {
                    ResolveArdyInitialHistory(
                        clip,
                        ardyProfile,
                        timelineClip,
                        out initialHistorySource);
                }
            }
            int effectiveSeed = effectiveSeedOverride ?? ResolveEffectiveSeed(clip);
            if (effectiveSeedOverride.HasValue && clip.seed != effectiveSeed)
            {
                clip.seed = effectiveSeed;
                EditorUtility.SetDirty(clip);
            }
            GameObject outputBindingObject = ConstraintProvider.FindTimelineBindingObjectForAsset(clip, timelineClip);
            PlayableDirector outputDirector = null;
            TrackAsset outputTrack = timelineClip.GetParentTrack();
            if (outputTrack != null)
            {
                KimodoInOutConstraintAdapter.TryResolveDirector(
                    timelineClip,
                    outputTrack,
                    out outputDirector,
                    out _);
            }
            KimodoEditorGenerateOutputPlan outputPlanSnapshot = KimodoTimelineGenerationOutputPlanner.Capture(
                clip,
                explicitRetargetAvatar: null,
                resolvedModelName,
                outputBindingObject);
            List<KimodoClipConstraint> clipConstraints = KimodoTimelineClipConstraintBuilder.Build(
                clip,
                timelineClip,
                resolvedModelName,
                runtimeFrameCount,
                targetFrameRate,
                runtimeTrimStartFrame,
                !disableTimelineInOut &&
                    (externalConstraint?.Enabled != true || externalConstraint.IncludeTimelineConstraints),
                token);
            KimodoPlayableClipGenerationSettings settings = KimodoPlayableClipGenerationSettings.instance;
            return new KimodoEditorGenerateRequest(
                () => KimodoTimelineGenerationOutputPlanner.CreateTargetClip(clip),
                (generatedClip, modelName) => KimodoTimelineGenerationOutputPlanner.Resolve(
                    outputPlanSnapshot,
                    outputBindingObject,
                    generatedClip,
                    modelName),
                outputPlanSnapshot)
            {
                Prompt = settings.ResolvePrompt(prompt),
                ModelName = resolvedModelName,
                TextEncoderMode = clip.textEncoderMode,
                TargetFrameCount = targetFrameCount,
                TargetFrameRate = targetFrameRate,
                RuntimeFrameCount = runtimeFrameCount,
                RuntimeTrimStartFrame = runtimeTrimStartFrame,
                DiffusionSteps = KimodoMotionModelProfiles.ClampDiffusionSteps(
                    resolvedModelName,
                    clip.diffusionSteps),
                EffectiveSeed = effectiveSeed,
                Constraints = new KimodoConstraintPayload { json = constraintsJson, clips = clipConstraints },
                DeferredInConstraintJson = deferredInConstraintJson,
                DeferredOutConstraintJson = deferredOutConstraintJson,
                AnalysisOptionsJson = string.IsNullOrWhiteSpace(externalConstraint?.AnalysisOptionsJson)
                    ? clip.analysisOptionsJson ?? string.Empty
                    : externalConstraint.AnalysisOptionsJson,
                ModelsRoot = settings.LocalModelsPath?.Trim() ?? string.Empty,
                Token = token,
                HasSyntheticAutoBeginConstraint = hasSyntheticAutoBeginConstraint,
                ConstraintSamples = constraintSamples,
                TimelineClipSnapshot = timelineClip,
                ResetTimelineTimeScaleAfterGeneration =
                    !disableTimelineInOut &&
                    (externalConstraint == null ||
                        !externalConstraint.Enabled ||
                        externalConstraint.IncludeTimelineConstraints) &&
                    clip.inOutConstraintMode == KimodoInOutConstraintMode.Inside &&
                    (clip.enableInConstraint || clip.enableOutConstraint) &&
                    !Mathf.Approximately((float)timelineClip.timeScale, 1f),
                UseRelativeOffset = clip.useRelativeOffset,
                RelativeOffset = clip.relativeOffset,
                TimelineDirectorSnapshot = outputDirector,
                TimelineContextSnapshot = timelineContextSnapshot,
                InitialArdyHistorySource = initialHistorySource,
                ArdyHistoryWeight = isArdy && !clip.ardyAutoHistory
                    ? Mathf.Clamp01(clip.ardyHistoryWeight)
                    : (double?)null
            };
        }

        private static string CombineConstraintJson(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first)) return second ?? string.Empty;
            if (string.IsNullOrWhiteSpace(second)) return first;
            var combined = JArray.Parse(first);
            combined.Merge(JArray.Parse(second));
            return combined.ToString(Newtonsoft.Json.Formatting.None);
        }

        // TODO: expose an internal generation entry that supplies an AvatarMask when Clip Constraint is ready.
        internal static bool TryGetClipConstraintAvatarMask(
            KimodoPlayableClip clip,
            out AvatarMask avatarMask)
        {
            avatarMask = null;
            return false;
        }

        private static bool ShouldUseOutsideGuardFrame(
            KimodoPlayableClip clip,
            KimodoExternalConstraintRequest externalConstraint,
            bool disableTimelineInOut)
        {
            return clip != null &&
                !disableTimelineInOut &&
                (externalConstraint?.Enabled != true || externalConstraint.IncludeTimelineConstraints) &&
                clip.inOutConstraintMode == KimodoInOutConstraintMode.Outside &&
                clip.enableInConstraint;
        }

        internal static bool IsLoopGenerationEnabled(
            KimodoPlayableClip clip,
            TimelineClip timelineClip = null)
        {
            if (clip == null || !clip.generateLoop)
            {
                return false;
            }

            TimelineClip resolved = timelineClip ?? KimodoTimelineClipResolver.FindTimelineClipForAsset(clip);
            return resolved != null &&
                resolved.duration > 0.0 &&
                Mathf.RoundToInt((float)(resolved.duration * 60.0)) <= 300;
        }

        public static void FinalizeGeneration(
            KimodoPlayableClip clip,
            KimodoEditorGenerateRequest request,
            KimodoEditorGenerationResult result)
        {
            if (clip == null || request == null || result == null || result.GeneratedClip == null)
            {
                return;
            }

            TimelineClip timelineClip = request.TimelineClipSnapshot ??
                KimodoTimelineClipResolver.FindTimelineClipForAsset(clip);
            int undoGroup = BeginReplaceTimelineAnimationUndo(clip, timelineClip);
            try
            {
                clip.clip = result.GeneratedClip;
                ApplyGeneratedMetadata(clip, result.Prompt, result.MotionJsonCompact);
                EditorUtility.SetDirty(clip);
                EditorUtility.SetDirty(result.GeneratedClip);
                result.ConstraintsPath = request.Constraints.IsEmpty ? "(none)" : "(inline-json)";
                HandleGeneratedClipWritebackCompleted(clip, request);

                if (!KimodoEditorClipWritebackService.TryMaterializeGeneratedClipCache(
                        result.GeneratedClip,
                        request.OutputPlan != null && request.OutputPlan.ExportMuscleClip,
                        request.OutputPlan != null ? request.OutputPlan.TargetRetargetAvatar : null,
                        forceRefresh: false,
                        out AnimationClip generatedCacheClip,
                        out string cacheError))
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(cacheError)
                            ? "Materialize generated clip cache failed."
                            : cacheError);
                }

                if (generatedCacheClip != null)
                {
                    EditorUtility.SetDirty(generatedCacheClip);
                }

                ResetTimelineTimeScaleAfterGeneration(request);
                result.ConstraintLoss = KimodoGenerationConstraintHipsComparer.Compare(
                    result.GeneratedClip,
                    timelineClip,
                    request.ModelName,
                    request.ConstraintSamples,
                    request.RuntimeTrimStartFrame,
                    request.TargetFrameRate,
                    request.TimelineContextSnapshot);
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        internal static bool ResetTimelineTimeScaleAfterGeneration(KimodoEditorGenerateRequest request)
        {
            TimelineClip timelineClip = request?.TimelineClipSnapshot;
            if (request?.ResetTimelineTimeScaleAfterGeneration != true ||
                timelineClip == null ||
                Mathf.Approximately((float)timelineClip.timeScale, 1f))
            {
                return false;
            }

            timelineClip.timeScale = 1.0;
            TrackAsset track = timelineClip.GetParentTrack();
            if (track != null)
            {
                EditorUtility.SetDirty(track);
                if (track.timelineAsset != null)
                {
                    EditorUtility.SetDirty(track.timelineAsset);
                }
            }

            if (TimelineEditor.inspectedAsset != null)
            {
                KimodoTimelinePreviewRefreshUtility.RefreshEditorWorkflow(RefreshReason.ContentsModified);
            }
            return true;
        }

        private static void ApplyGeneratedMetadata(KimodoPlayableClip clip, string prompt, string motionJson)
        {
            if (clip == null || string.IsNullOrWhiteSpace(motionJson))
            {
                return;
            }

            JObject obj = JObject.Parse(motionJson);
            clip.lastGeneratedPrompt = prompt ?? string.Empty;
            clip.isGenerated = true;
            clip.frameCount = obj.Value<int?>("num_frames") ?? 0;
            clip.jointCount = obj.Value<int?>("num_joints") ?? 0;
            clip.fps = Mathf.RoundToInt(obj.Value<float?>("fps") ?? KimodoMotionModelProfiles.DefaultFrameRate);
        }

        private static void HandleGeneratedClipWritebackCompleted(
            KimodoPlayableClip playableClip,
            KimodoEditorGenerateRequest request)
        {
            if (playableClip != null)
            {
                if (playableClip.useRelativeOffset && playableClip.clip != null)
                {
                    playableClip.position = ResolveRelativeOffsetInTrackSpace(playableClip, request);
                }
                else
                {
                    playableClip.position = Vector3.zero;
                }
                playableClip.rotation = Quaternion.identity;
                EditorUtility.SetDirty(playableClip);
            }
            KimodoTimelinePreviewRefreshUtility.RefreshIfPreviewing();
        }

        internal static Vector3 ResolveRelativeOffsetInTrackSpace(
            KimodoPlayableClip playableClip,
            KimodoEditorGenerateRequest request)
        {
            Vector3 worldOffset = playableClip != null ? playableClip.relativeOffset : Vector3.zero;
            TimelineClip timelineClip = KimodoTimelineClipResolver.FindTimelineClipForAsset(playableClip);
            if (!(timelineClip?.GetParentTrack() is AnimationTrack track)) return worldOffset;

            Vector3 trackPosition;
            Quaternion trackRotation;
            KimodoTimelineInOutConstraintContext context = request?.TimelineContextSnapshot;
            if (context != null && ReferenceEquals(context.Track, track) && context.HasTrackOffsetSnapshot)
            {
                trackPosition = context.TrackOffsetPosition;
                trackRotation = context.TrackOffsetRotation;
            }
            else
            {
                if (!KimodoInOutConstraintAdapter.TryResolveTimelineContext(timelineClip, out context, out _) || context == null)
                    return worldOffset;
                KimodoTimelineTrackOffsetUtility.CaptureWorldOffset(
                    context.Track,
                    context.Animator,
                    out trackPosition,
                    out trackRotation,
                    out _);
            }

            KimodoTimelineTrackOffsetUtility.WorldToTrackPose(
                worldOffset,
                Quaternion.identity,
                trackPosition,
                trackRotation,
                out Vector3 trackLocalOffset,
                out _);
            return trackLocalOffset;
        }

        internal static void NormalizeRootTranslationToFrameZero(AnimationClip clip)
        {
            if (clip == null) return;
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            string transformRootPath = null;
            AnimationCurve transformX = null;
            AnimationCurve transformY = null;
            AnimationCurve transformZ = null;
            var transformCurves = new Dictionary<string, AnimationCurve[]>();
            AnimationCurve animatorX = null;
            AnimationCurve animatorY = null;
            AnimationCurve animatorZ = null;
            foreach (EditorCurveBinding binding in bindings)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (binding.type == typeof(Transform) &&
                    (binding.propertyName == "m_LocalPosition.x" ||
                     binding.propertyName == "m_LocalPosition.y" ||
                     binding.propertyName == "m_LocalPosition.z"))
                {
                    if (!transformCurves.TryGetValue(binding.path, out AnimationCurve[] curves))
                    {
                        curves = new AnimationCurve[3];
                        transformCurves.Add(binding.path, curves);
                    }
                    if (binding.propertyName == "m_LocalPosition.x") curves[0] = curve;
                    else if (binding.propertyName == "m_LocalPosition.y") curves[1] = curve;
                    else curves[2] = curve;
                }
                else if (binding.type == typeof(Animator) && binding.path == string.Empty)
                {
                    if (binding.propertyName == "RootT.x") animatorX = curve;
                    else if (binding.propertyName == "RootT.y") animatorY = curve;
                    else if (binding.propertyName == "RootT.z") animatorZ = curve;
                }
            }

            foreach (KeyValuePair<string, AnimationCurve[]> pair in transformCurves)
            {
                AnimationCurve[] curves = pair.Value;
                if (curves[0] == null || curves[1] == null || curves[2] == null) continue;
                if (transformRootPath == null || CountPathDepth(pair.Key) < CountPathDepth(transformRootPath))
                {
                    transformRootPath = pair.Key;
                    transformX = curves[0];
                    transformY = curves[1];
                    transformZ = curves[2];
                }
            }

            bool useTransformRoot = transformX != null && transformY != null && transformZ != null;
            AnimationCurve x = useTransformRoot ? transformX : animatorX;
            AnimationCurve y = useTransformRoot ? transformY : animatorY;
            AnimationCurve z = useTransformRoot ? transformZ : animatorZ;
            string path = useTransformRoot ? transformRootPath : string.Empty;
            Type bindingType = useTransformRoot ? typeof(Transform) : typeof(Animator);
            string xProperty = bindingType == typeof(Transform) ? "m_LocalPosition.x" : "RootT.x";
            string yProperty = bindingType == typeof(Transform) ? "m_LocalPosition.y" : "RootT.y";
            string zProperty = bindingType == typeof(Transform) ? "m_LocalPosition.z" : "RootT.z";
            if (x == null || y == null || z == null || x.length == 0 || y.length == 0 || z.length == 0) return;
            Vector3 origin = new Vector3(x.Evaluate(0f), y.Evaluate(0f), z.Evaluate(0f));
            OffsetCurve(x, origin.x);
            OffsetCurve(y, origin.y);
            OffsetCurve(z, origin.z);
            clip.SetCurve(path, bindingType, xProperty, x);
            clip.SetCurve(path, bindingType, yProperty, y);
            clip.SetCurve(path, bindingType, zProperty, z);
            EditorUtility.SetDirty(clip);
        }

        private static int CountPathDepth(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            int depth = 1;
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] == '/') depth++;
            }
            return depth;
        }

        private static void OffsetCurve(AnimationCurve curve, float offset)
        {
            if (curve == null || Mathf.Approximately(offset, 0f)) return;
            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++) keys[i].value -= offset;
            curve.keys = keys;
        }

        private static int BeginReplaceTimelineAnimationUndo(
            KimodoPlayableClip playableClip,
            TimelineClip timelineClip)
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(ReplaceTimelineAnimationUndoName);
            Undo.RecordObject(playableClip, ReplaceTimelineAnimationUndoName);

            if (timelineClip != null)
            {
                UndoExtensions.RegisterClip(timelineClip, L10n.Tr(ReplaceTimelineAnimationUndoName));

                TrackAsset parentTrack = timelineClip.GetParentTrack();
                if (parentTrack != null)
                {
                    Undo.RecordObject(parentTrack, ReplaceTimelineAnimationUndoName);
                }
            }

            if (TimelineEditor.inspectedAsset != null)
            {
                Undo.RecordObject(TimelineEditor.inspectedAsset, ReplaceTimelineAnimationUndoName);
            }

            return undoGroup;
        }

        private static int ResolveEffectiveSeed(KimodoPlayableClip clip)
        {
            int effectiveSeed = clip.randomSeed
                ? Guid.NewGuid().GetHashCode() & int.MaxValue
                : clip.seed;

            if (clip.randomSeed || clip.seed != effectiveSeed)
            {
                clip.seed = effectiveSeed;
                EditorUtility.SetDirty(clip);
            }

            return effectiveSeed;
        }

        private static void ResolveArdyInitialHistory(
            KimodoPlayableClip clip,
            KimodoMotionModelProfile profile,
            TimelineClip timelineClipOverride,
            out ArdyEditorHistorySource source)
        {
            source = null;
            if (clip.inOutConstraintMode != KimodoInOutConstraintMode.Outside || !clip.enableInConstraint)
            {
                return;
            }

            TimelineClip timelineClip = timelineClipOverride ?? KimodoTimelineClipResolver.FindTimelineClipForAsset(clip);
            if (timelineClip == null ||
                !KimodoInOutConstraintAdapter.TryResolveTimelineContext(
                    timelineClip,
                    out KimodoTimelineInOutConstraintContext context,
                    out _))
            {
                return;
            }

            if (context.PreviousTimelineClip == null || context.PreviousTimelineClip.duration <= 0.0)
            {
                return;
            }

            source = new ArdyEditorHistorySource
            {
                TimelineContext = context,
                RangeStartSeconds = Math.Max(
                    0.0,
                    timelineClip.start - (profile.MaxContextFrames - profile.HorizonFrames) / profile.SourceFps),
                RangeEndSeconds = Math.Max(0.0, timelineClip.start)
            };
        }

    }
}
