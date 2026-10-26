using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoBridge
{
    [Serializable]
    public sealed class KimodoDebugRollClip : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField] private string passName = string.Empty;
        [SerializeField, TextArea] private string addedConstraintsJson = string.Empty;
        [SerializeField, TextArea] private string combinedConstraintsJson = string.Empty;
        [SerializeField] private string modelName = string.Empty;
        [SerializeField] private int frameRate;
        [SerializeField] private int frameCount;

        public ClipCaps clipCaps => ClipCaps.None;

        public override double duration => 1.0;

        public void SetPassData(
            string name,
            string addedJson,
            string combinedJson,
            string model,
            int fps,
            int frames)
        {
            passName = name ?? string.Empty;
            addedConstraintsJson = addedJson ?? string.Empty;
            combinedConstraintsJson = combinedJson ?? string.Empty;
            modelName = model ?? string.Empty;
            frameRate = fps;
            frameCount = frames;
        }

        public string PassName => passName;
        public string AddedConstraintsJson => addedConstraintsJson;
        public string CombinedConstraintsJson => combinedConstraintsJson;
        public string ModelName => modelName;
        public int FrameRate => frameRate;
        public int FrameCount => frameCount;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<KimodoDebugRollBehaviour>.Create(graph);
        }
    }

    public sealed class KimodoDebugRollBehaviour : PlayableBehaviour
    {
    }

    [TrackClipType(typeof(KimodoDebugRollClip))]
    public sealed class KimodoDebugRollTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<KimodoDebugRollBehaviour>.Create(graph, inputCount);
        }
    }
}
