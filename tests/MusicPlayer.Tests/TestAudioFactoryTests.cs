using System;
using Xunit;

namespace MusicPlayer.Tests;

public class TestAudioFactoryTests
{
    [Fact]
    public void GenerateSilence_ReturnsOnlyZeroSamples()
    {
        float[] samples =
            TestAudioFactory.GenerateSilence(
                sampleRate: 1000,
                durationSeconds: 2);

        Assert.Equal(2000, samples.Length);

        Assert.All(samples, sample =>
            Assert.Equal(0f, sample));
    }

    [Fact]
    public void GenerateSineWave_ReturnsCorrectSampleCount()
    {
        float[] samples =
            TestAudioFactory.GenerateSineWave(
                frequencyHz: 440,
                sampleRate: 44100,
                durationSeconds: 1);

        Assert.Equal(44100, samples.Length);
    }

    [Fact]
    public void GenerateSineWave_ProducesExpectedRms()
    {
        float[] samples =
            TestAudioFactory.GenerateSineWave(
                frequencyHz: 440,
                amplitude: 0.5,
                sampleRate: 44100,
                durationSeconds: 1);

        double meanSquare = 0;

        foreach (float sample in samples)
            meanSquare += sample * (double)sample;

        double rms = Math.Sqrt(meanSquare / samples.Length);

        Assert.InRange(rms, 0.3535, 0.3536);
    }

    [Fact]
    public void GenerateNoise_WithSameSeed_IsRepeatable()
    {
        float[] first =
            TestAudioFactory.GenerateNoise(
                seed: 42,
                sampleRate: 1000);

        float[] second =
            TestAudioFactory.GenerateNoise(
                seed: 42,
                sampleRate: 1000);

        Assert.Equal(first, second);
    }

    [Fact]
    public void GenerateBeatPulses_ReturnsCorrectLength()
    {
        float[] samples =
            TestAudioFactory.GenerateBeatPulses(
                bpm: 120,
                durationSeconds: 5,
                sampleRate: 44100);

        Assert.Equal(220500, samples.Length);
        Assert.Contains(samples, sample => sample != 0f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void GenerateBeatPulses_RejectsInvalidBpm(double bpm)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TestAudioFactory.GenerateBeatPulses(bpm));
    }
}
