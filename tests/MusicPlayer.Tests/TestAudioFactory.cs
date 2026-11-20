using System;

namespace MusicPlayer.Tests;

/// <summary>
/// Generates predictable audio samples for automated tests.
///
/// Each sample is a float representing an audio amplitude.
/// The normal PCM range is approximately -1.0 to +1.0.
///
/// The methods return raw mono audio samples, not WAV files.
/// </summary>
public static class TestAudioFactory
{
    /// <summary>
    /// Generate silence containing the specified number of seconds.
    /// </summary>
    public static float[] GenerateSilence(
        int sampleRate = 44100,
        double durationSeconds = 1.0)
    {
        int sampleCount = GetSampleCount(sampleRate, durationSeconds);

        // A new float array starts with all values set to zero.
        return new float[sampleCount];
    }

    /// <summary>
    /// Generate a sine wave with a known frequency and amplitude.
    ///
    /// Example: 440 Hz at 50% amplitude for one second.
    /// </summary>
    public static float[] GenerateSineWave(
        double frequencyHz,
        double amplitude = 0.5,
        int sampleRate = 44100,
        double durationSeconds = 1.0)
    {
        if (!double.IsFinite(frequencyHz) ||
            frequencyHz <= 0 ||
            frequencyHz >= sampleRate / 2.0)
        {
            throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        }

        ValidateAmplitude(amplitude);

        int sampleCount = GetSampleCount(sampleRate, durationSeconds);
        float[] samples = new float[sampleCount];

        // x[n] = A * sin(2*pi*f*n / Fs)
        for (int i = 0; i < sampleCount; i++)
        {
            double time = (double)i / sampleRate;

            samples[i] = (float)(
                amplitude *
                Math.Sin(2.0 * Math.PI * frequencyHz * time));
        }

        return samples;
    }

    /// <summary>
    /// Generate short bursts at a specific tempo.
    ///
    /// A small 1 kHz tone is used for each beat, instead of
    /// a single-sample impulse, so it resembles audio transients.
    /// </summary>
    public static float[] GenerateBeatPulses(
        double bpm,
        double durationSeconds = 10.0,
        int sampleRate = 44100,
        double amplitude = 0.8)
    {
        if (!double.IsFinite(bpm) || bpm <= 0)
            throw new ArgumentOutOfRangeException(nameof(bpm));

        ValidateAmplitude(amplitude);

        int sampleCount = GetSampleCount(sampleRate, durationSeconds);
        float[] samples = new float[sampleCount];

        double beatIntervalSeconds = 60.0 / bpm;

        // Each beat is a short decaying tone lasting 40 ms.
        int burstSamples = Math.Max(1, (int)(sampleRate * 0.04));

        for (double beatTime = 0;
             beatTime < durationSeconds;
             beatTime += beatIntervalSeconds)
        {
            int startIndex = (int)Math.Round(beatTime * sampleRate);

            for (int j = 0; j < burstSamples; j++)
            {
                int index = startIndex + j;

                if (index >= samples.Length)
                    break;

                // Exponentially decay each transient.
                double envelope = Math.Exp(-6.0 * j / burstSamples);
                double tone = Math.Sin(
                    2.0 * Math.PI * 1000.0 * j / sampleRate);

                samples[index] += (float)(
                    amplitude * envelope * tone);
            }
        }

        return samples;
    }

    /// <summary>
    /// Generate repeatable random noise.
    ///
    /// Fixed seed ensures the samples remain the same every run.
    /// </summary>
    public static float[] GenerateNoise(
        int seed = 12345,
        double amplitude = 0.5,
        int sampleRate = 44100,
        double durationSeconds = 1.0)
    {
        ValidateAmplitude(amplitude);

        int sampleCount = GetSampleCount(sampleRate, durationSeconds);
        float[] samples = new float[sampleCount];

        Random random = new(seed);

        for (int i = 0; i < sampleCount; i++)
        {
            // Random.NextDouble() returns [0, 1).
            // Transform into [-amplitude, +amplitude).
            samples[i] = (float)(
                (2.0 * random.NextDouble() - 1.0) * amplitude);
        }

        return samples;
    }

    /// <summary>
    /// Check that a requested amplitude is finite and in range.
    /// </summary>
    private static void ValidateAmplitude(double amplitude)
    {
        if (!double.IsFinite(amplitude) ||
            amplitude < 0 ||
            amplitude > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude));
        }
    }

    /// <summary>
    /// Convert a duration into a number of audio samples.
    /// </summary>
    private static int GetSampleCount(
        int sampleRate,
        double durationSeconds)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        if (!double.IsFinite(durationSeconds) ||
            durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds));
        }

        double count = sampleRate * durationSeconds;

        if (count > int.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds));

        int result = (int)Math.Round(count);

        if (result == 0)
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds));

        return result;
    }
}
