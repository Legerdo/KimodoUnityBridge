using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoBridge.Editor
{
    internal static class KimodoDebugTimelineWriter
    {
        internal const string DebugFolder = "Assets/KimodoGeneratedClips/Debug";

        internal sealed class RawClipInput
        {
            internal KimodoPlayableClip SourceClip;
            internal KimodoEditorGenerateRequest Request;
            internal KimodoEditorGenerationResult Result;
        }

        internal static bool TryCopyTimelineTiming(TimelineClip source, TimelineClip target)
        {
            if (source == null || target == null) return false;
            target.start = source.start;
            target.duration = source.duration;
            target.clipIn = source.clipIn;
            target.timeScale = source.timeScale;
            return true;
        }

        internal static void TryWrite(
            KimodoPlayableClip sourceClip,
            KimodoEditorGenerateRequest request,
            KimodoEditorGenerationResult result)
        {
            TryWriteGroup(new List<RawClipInput>
            {
                new RawClipInput { SourceClip = sourceClip, Request = request, Result = result }
            });
        }

        internal static void TryWriteGroup(IList<RawClipInput> inputs)
        {
            if (!KimodoPlayableClipGenerationSettings.instance.WriteResampledTimelineCacheClips) return;
            try
            {
                List<RawClipInput> validInputs = (inputs ?? Array.Empty<RawClipInput>())
                    .Where(input => input?.Request?.TimelineClipSnapshot != null && input.Result?.RawBoneClip != null)
                    .ToList();
                if (validInputs.Count == 0) return;

                string modelName = KimodoMotionModelProfiles.NormalizeName(validInputs[0].Request.ModelName);
                TimelineClip sourceTimelineClip = validInputs[0].Request.TimelineClipSnapshot;
                TrackAsset sourceTrack = sourceTimelineClip.GetParentTrack();
                TimelineAsset timeline = sourceTrack != null ? sourceTrack.timelineAsset : null;
                if (timeline == null)
                    throw new InvalidOperationException("The source Timeline is unavailable.");
                if (!KimodoDirectorResolver.TryResolve(timeline, out PlayableDirector director, out string directorError))
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(directorError)
                        ? "The source Timeline director is unavailable."
                        : directorError);

                for (int i = 0; i < validInputs.Count; i++)
                {
                    RawClipInput input = validInputs[i];
                    TrackAsset inputTrack = input.Request.TimelineClipSnapshot.GetParentTrack();
                    string inputModel = KimodoMotionModelProfiles.NormalizeName(input.Request.ModelName);
                    if (inputTrack == null || inputTrack.timelineAsset != timeline ||
                        !string.Equals(inputModel, modelName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("A shared Debug track requires clips from the same Timeline and profile model.");
                    }
                }

                string modelPath = AssetDatabase.GUIDToAssetPath(KimodoRuntimeProfileSkeletonPreview.ResolveModelGuid(modelName));
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (model == null) throw new InvalidOperationException($"Profile model for '{modelName}' was not found at '{modelPath}'.");

                var compatibleInputs = new List<RawClipInput>(validInputs.Count);
                for (int i = 0; i < validInputs.Count; i++)
                {
                    if (KimodoEditorClipUtility.CanApplyClipDirectlyToProfileSkeleton(
                            validInputs[i].Result.RawBoneClip, model, modelName, out string bindingError))
                    {
                        compatibleInputs.Add(validInputs[i]);
                        continue;
                    }

                    Debug.LogWarning($"[Kimodo][DebugTimeline] Skipped raw clip '{validInputs[i].SourceClip?.name}': it cannot drive the profile model: {bindingError}");
                }
                validInputs = compatibleInputs;
                if (validInputs.Count == 0) return;

                EnsureDebugFolder();
                GameObject instance = UnityEngine.Object.Instantiate(model);
                instance.name = $"KimodoDebugProfile_{modelName}_{DateTime.Now:yyyyMMdd_HHmmss_fff}";
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.enabled = true;

                AnimationTrack debugTrack = timeline.CreateTrack<AnimationTrack>(null, $"Debug_{modelName}_{DateTime.Now:HHmmss_fff}");
                debugTrack.trackOffset = TrackOffset.ApplyTransformOffsets;
                director.SetGenericBinding(debugTrack, animator);

                for (int i = 0; i < validInputs.Count; i++)
                {
                    RawClipInput input = validInputs[i];
                    AnimationClip sourceRawClip = input.Result.RawBoneClip;
                    try
                    {
                        string assetName = KimodoRuntimeUtility.SanitizeName(
                            $"Kimodo_DebugRaw_{input.SourceClip?.name}_{Guid.NewGuid():N}", "Kimodo_DebugRaw");
                        AnimationClip debugClip = new AnimationClip
                        {
                            name = assetName,
                            frameRate = sourceRawClip.frameRate,
                            legacy = sourceRawClip.legacy
                        };
                        KimodoEditorClipUtility.CopyClipData(sourceRawClip, debugClip, forceNoLoopKeepY: false);
                        debugClip.frameRate = sourceRawClip.frameRate;
                        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{DebugFolder}/{assetName}.anim");
                        AssetDatabase.CreateAsset(debugClip, assetPath);
                        AssetDatabase.SaveAssetIfDirty(debugClip);

                        TimelineClip debugTimelineClip = debugTrack.CreateClip<AnimationPlayableAsset>();
                        var playableAsset = (AnimationPlayableAsset)debugTimelineClip.asset;
                        playableAsset.clip = debugClip;
                        playableAsset.removeStartOffset = false;
                        debugTimelineClip.displayName = $"Raw_{input.SourceClip?.name ?? modelName}";
                        TryCopyTimelineTiming(input.Request.TimelineClipSnapshot, debugTimelineClip);
                        EditorUtility.SetDirty(debugClip);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Kimodo][DebugTimeline] Skipped raw clip '{input.SourceClip?.name}': {ex.GetType().Name}: {ex.Message}");
                    }
                }

                EditorUtility.SetDirty(instance);
                EditorUtility.SetDirty(animator);
                EditorUtility.SetDirty(debugTrack);
                EditorUtility.SetDirty(timeline);
                EditorUtility.SetDirty(director);
                EditorSceneManager.MarkSceneDirty(instance.scene);
                Debug.Log($"[Kimodo][DebugTimeline] Wrote {validInputs.Count} raw clip(s) to track '{debugTrack.name}'.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Kimodo][DebugTimeline] Skipped raw clip output: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void EnsureDebugFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/KimodoGeneratedClips"))
                AssetDatabase.CreateFolder("Assets", "KimodoGeneratedClips");
            if (!AssetDatabase.IsValidFolder(DebugFolder))
                AssetDatabase.CreateFolder("Assets/KimodoGeneratedClips", "Debug");
        }
    }
}
