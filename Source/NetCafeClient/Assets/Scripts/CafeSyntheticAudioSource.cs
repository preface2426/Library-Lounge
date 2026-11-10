#if NETCAFE_B1 && (UNITY_EDITOR || DEVELOPMENT_BUILD)
using System;
using LiveKit;

// Test-only deterministic PCM tone. No microphone device, OS input, music,
// desktop contents or credentials are accessed or recorded.
public sealed class CafeSyntheticAudioSource : RtcAudioSource
{
    public override event Action<float[], int, int> AudioRead;
    readonly float[] samples = new float[480];
    long frame;

    public CafeSyntheticAudioSource() : base(RtcAudioSourceType.AudioSourceCustom, 48000u, 1u) {}

    public void Pump()
    {
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (float)(.035 * Math.Sin((frame * 480 + i) * 2 * Math.PI * 220 / 48000));
        frame++;
        AudioRead?.Invoke(samples, 1, 48000);
    }
}
#endif
