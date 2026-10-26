namespace KimodoBridge.Editor
{
    internal static class KimodoDebugRollTrackUtility
    {
        internal static void ConfigureClip(
            KimodoDebugRollClip rollClip,
            KimodoDebugConstraintPass pass)
        {
            if (rollClip == null || pass == null) return;
            rollClip.SetPassData(
                pass.Name,
                pass.AddedConstraintsJson,
                pass.CombinedConstraintsJson,
                pass.ModelName,
                pass.FrameRate,
                pass.FrameCount);
        }
    }
}

