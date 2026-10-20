namespace RunicLilyPads
{
    internal static class WaterfallSoundRules
    {
        // Height is the upper-to-lower water-level drop, not listener distance.
        internal static int Track(float drop,float threshold)=>drop>=threshold?10:9;
    }
}
