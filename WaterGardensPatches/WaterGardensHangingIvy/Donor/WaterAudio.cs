using UnityEngine;
namespace RunicLilyPads
{
    // Use Valheim's ambient mixer so game master/effects volume applies once.
    internal static class WaterAudio
    {
        internal static void SetVolume(AudioSource source, float target, float step = 0)
        {
            if (!source.outputAudioMixerGroup && AudioMan.instance)
                source.outputAudioMixerGroup = AudioMan.instance.m_ambientMixer;
            source.volume = target <= 0 ? 0 : step > 0 ? Mathf.MoveTowards(source.volume, target, step) : target;
        }
    }
}
