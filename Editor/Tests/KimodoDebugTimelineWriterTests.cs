using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;

namespace KimodoBridge.Editor.Tests
{
    public sealed class KimodoDebugTimelineWriterTests
    {
        [Test]
        public void TryCopyTimelineTiming_CopiesClipRangeAndPlaybackScale()
        {
            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AnimationTrack track = timeline.CreateTrack<AnimationTrack>();
            TimelineClip source = track.CreateClip<AnimationPlayableAsset>();
            TimelineClip target = track.CreateClip<AnimationPlayableAsset>();
            try
            {
                source.start = 2.5;
                source.duration = 4.0;
                source.clipIn = 0.25;
                source.timeScale = 1.5;

                Assert.That(KimodoDebugTimelineWriter.TryCopyTimelineTiming(source, target), Is.True);
                Assert.That(target.start, Is.EqualTo(source.start));
                Assert.That(target.duration, Is.EqualTo(source.duration));
                Assert.That(target.clipIn, Is.EqualTo(source.clipIn));
                Assert.That(target.timeScale, Is.EqualTo(source.timeScale));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }
    }
}
