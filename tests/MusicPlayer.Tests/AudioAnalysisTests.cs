using System;
using System.IO;
using System.Threading;
using MusicPlayer;
using NAudio.Wave;
using Xunit;

namespace MusicPlayer.Tests;

public class AudioAnalysisTests
{
    // The sample rate used for all of our test signals.
    private const int SampleRate = 44100;

    // -------------------------------------------------
    // TEST 1: SILENCE
    // -------------------------------------------------

    [Fact]
    public void AnalyzeSignal_Silence_HasNoFrequencyEnergy()
    {
        float[] samples =
            TestAudioFactory.GenerateSilence(
                sampleRate: SampleRate);

        using var fixture = new TemporaryWavFile(
            samples, SampleRate);

        var service = new AudioAnalysisService();

        FrequencyAnalysis result =
            service.AnalyzeSignal(
                fixture.FilePath,
                CancellationToken.None);

        Assert.NotEmpty(result.Frames);

        foreach (FrequencyFrame frame in result.Frames)
        {
            Assert.Equal(0, frame.Bass);
            Assert.Equal(0, frame.Midrange);
            Assert.Equal(0, frame.Treble);

            // Silence is -infinity on a dBFS scale.
            Assert.Equal(
                double.NegativeInfinity,
                frame.RmsDbfs);

            Assert.Equal(
                double.NegativeInfinity,
                frame.PeakDbfs);
        }
    }

    // -------------------------------------------------
    // TEST 2: RMS AND PEAK
    // -------------------------------------------------

    [Fact]
    public void AnalyzeSignal_SineWave_ReportsCorrectLevels()
    {
        // Generate a 1 kHz signal at amplitude 0.5.
        float[] samples =
            TestAudioFactory.GenerateSineWave(
                frequencyHz: 1000,
                amplitude: 0.5,
                sampleRate: SampleRate);

        using var fixture = new TemporaryWavFile(
            samples, SampleRate);

        var service = new AudioAnalysisService();

        FrequencyAnalysis result =
            service.AnalyzeSignal(
                fixture.FilePath,
                CancellationToken.None);

        Assert.NotEmpty(result.Frames);

        // Use the first complete analysis frame.
        FrequencyFrame frame = result.Frames[0];

        // RMS = 0.5 / sqrt(2)
        // RMS dBFS is approximately -9.03.
        Assert.InRange(
            frame.RmsDbfs,
            -9.4,
            -8.7);

        // Peak amplitude 0.5 is approximately -6.02 dBFS.
        Assert.InRange(
            frame.PeakDbfs,
            -6.2,
            -5.9);
    }

    // -------------------------------------------------
    // TEST 3: FREQUENCY CLASSIFICATION
    // -------------------------------------------------

    [Theory]
    [InlineData(100, "Bass")]
    [InlineData(1000, "Midrange")]
    [InlineData(8000, "Treble")]
    public void AnalyzeSignal_SineWave_DominantBandIsCorrect(
        double frequencyHz,
        string expectedBand)
    {
        float[] samples =
            TestAudioFactory.GenerateSineWave(
                frequencyHz: frequencyHz,
                amplitude: 0.5,
                sampleRate: SampleRate);

        using var fixture = new TemporaryWavFile(
            samples, SampleRate);

        var service = new AudioAnalysisService();

        FrequencyAnalysis result =
            service.AnalyzeSignal(
                fixture.FilePath,
                CancellationToken.None);

        Assert.NotEmpty(result.Frames);

        FrequencyFrame frame = result.Frames[0];

        // Determine which band contains the most energy.
        string dominantBand = "Bass";
        double maximumEnergy = frame.Bass;

        if (frame.Midrange > maximumEnergy)
        {
            dominantBand = "Midrange";
            maximumEnergy = frame.Midrange;
        }

        if (frame.Treble > maximumEnergy)
        {
            dominantBand = "Treble";
        }

        Assert.Equal(expectedBand, dominantBand);

        // Most energy should be concentrated in that band.
        double dominantEnergy = expectedBand switch
        {
            "Bass" => frame.Bass,
            "Midrange" => frame.Midrange,
            "Treble" => frame.Treble,
            _ => throw new ArgumentException(
                "Unknown frequency band.")
        };

        Assert.True(
            dominantEnergy > 0.90,
            $"Expected {expectedBand} to contain over 90% " +
            $"of the energy, but got {dominantEnergy:P2}.");
    }

    // -------------------------------------------------
    // TEST 4: FRAME DURATION
    // -------------------------------------------------

    [Fact]
    public void AnalyzeSignal_ReportsExpectedFrameDuration()
    {
        float[] samples =
            TestAudioFactory.GenerateSilence(
                sampleRate: SampleRate);

        using var fixture = new TemporaryWavFile(
            samples, SampleRate);

        var service = new AudioAnalysisService();

        FrequencyAnalysis result =
            service.AnalyzeSignal(
                fixture.FilePath,
                CancellationToken.None);

        // Your analyzer uses an FFT size of 4096.
        double expected = 4096.0 / SampleRate;

        Assert.Equal(
            expected,
            result.SecondsPerFrame,
            precision: 8);
    }

    // -------------------------------------------------
    // TEST 5: CANCELLATION
    // -------------------------------------------------

    [Fact]
    public void AnalyzeSignal_CanceledToken_Throws()
    {
        float[] samples =
            TestAudioFactory.GenerateSilence(
                sampleRate: SampleRate);

        using var fixture = new TemporaryWavFile(
            samples, SampleRate);

        var service = new AudioAnalysisService();

        using var source =
            new CancellationTokenSource();

        source.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            service.AnalyzeSignal(
                fixture.FilePath,
                source.Token));
    }

    // -------------------------------------------------
    // TEST HELPER: TEMPORARY WAV FILE
    // -------------------------------------------------

    // AudioAnalysisService reads file paths, not float arrays.
    // This helper writes our generated signals to temporary
    // WAV files and deletes them after each test.

    private sealed class TemporaryWavFile : IDisposable
    {
        public string FilePath { get; }

        public TemporaryWavFile(
            float[] samples,
            int sampleRate)
        {
            FilePath = Path.Combine(
                Path.GetTempPath(),
                $"MusicPlayerTest_{Guid.NewGuid():N}.wav");

            // Store the samples in 32-bit floating-point WAV.
            var format =
                WaveFormat.CreateIeeeFloatWaveFormat(
                    sampleRate, 1);

            using var writer =
                new WaveFileWriter(FilePath, format);

            writer.WriteSamples(
                samples, 0, samples.Length);
        }

        public void Dispose()
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
    }
}
