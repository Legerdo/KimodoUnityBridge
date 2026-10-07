using System;
using System.Collections.Generic;
using KimodoUnityBridge;

namespace KimodoBridge
{
    /// <summary>Validates one canonical constraint sample per frame.</summary>
    public static class KimodoConstraintSampleComposer
    {
        public static List<KimodoMarkerSampleResult> ComposeCanonicalSamples(
            IReadOnlyList<KimodoMarkerSampleResult> samples, double frameRate)
        {
            var output = new List<KimodoMarkerSampleResult>();
            if (samples == null || frameRate <= 0.0) return output;
            var groups = new SortedDictionary<int, List<KimodoMarkerSampleResult>>();
            for (int i = 0; i < samples.Count; i++)
            {
                KimodoMarkerSampleResult sample = samples[i]?.Clone();
                if (sample == null || !sample.enabled) continue;
                if (!sample.sampleValid) throw new InvalidOperationException("Constraint sample is invalid.");
                if (sample.creationOrder == 0) sample.creationOrder = i + 1L;
                sample.enableMask ??= new KimodoConstraintMask();
                int frame = KimodoFrameTimeUtility.SecondsToFrameIndex(sample.sampleTime, frameRate);
                if (!groups.TryGetValue(frame, out List<KimodoMarkerSampleResult> group))
                {
                    group = new List<KimodoMarkerSampleResult>();
                    groups.Add(frame, group);
                }
                group.Add(sample);
            }
            foreach (KeyValuePair<int, List<KimodoMarkerSampleResult>> pair in groups)
            {
                if (pair.Value.Count != 1)
                    throw new InvalidOperationException($"Multiple constraints share frame {pair.Key}; this is undefined.");
                KimodoMarkerSampleResult sample = pair.Value[0];
                sample.constraintMode = KimodoConstraintInternal.ResolveExecutionMode(sample);
                output.Add(sample);
            }
            return output;
        }

        public static KimodoMarkerSampleResult ResolveUnifiedSample(KimodoMarkerSampleResult sample)
        {
            if (sample == null) return null;
            List<KimodoMarkerSampleResult> composed = ComposeCanonicalSamples(new[] { sample }, 30.0);
            return composed.Count == 0 ? sample.Clone() : composed[0];
        }

        public static List<KimodoMarkerSampleResult> ExpandProtocolSamples(
            IReadOnlyList<KimodoMarkerSampleResult> samples, double frameRate) =>
            ComposeCanonicalSamples(samples, frameRate);

        public static List<KimodoMarkerSampleResult> MergeAsUnifiedSamples(
            IReadOnlyList<KimodoMarkerSampleResult> samples, double frameRate) =>
            ComposeCanonicalSamples(samples, frameRate);
    }
}
