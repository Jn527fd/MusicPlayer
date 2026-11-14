using System;
using System.IO;
using NAudio.Dsp;
using System.Linq;
using NAudio.Wave;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace MusicPlayer;

// A simple immutable group of metadata values consumed by SongFile.
public sealed record AudioMetadata(
    string BpmText,
    string LengthText,
    string AudioDetails);


public sealed record FrequencyFrame(
    double Bass,
    double Midrange,
    double Treble,
    double RmsDbfs,
    double PeakDbfs
);

public sealed record TempoEstimate(
    double Bpm,
    double RhythmStrength
);

public sealed record FrequencyAnalysis(
    double SecondsPerFrame,
    FrequencyFrame[] Frames
);

public sealed class AudioAnalysisService
{
    private const int WaveformBins = 400;

    // Existing step 2 behavior: read tags and technical audio properties.
    public AudioMetadata ReadMetadata(string fullPath)
    {
        string format = Path.GetExtension(fullPath).TrimStart('.').ToUpperInvariant();

        try
        {
            using var file = TagLib.File.Create(fullPath);
            TimeSpan duration = file.Properties.Duration;
            string lengthText = duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss")
                : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

            uint bpm = file.Tag.BeatsPerMinute;
            string bpmText = bpm > 0 ? bpm.ToString() : "—";

            List<string> details = new() { format };
            int sampleRate = file.Properties.AudioSampleRate;
            int channels = file.Properties.AudioChannels;
            int bitrate = file.Properties.AudioBitrate;

            if (sampleRate > 0)
                details.Add($"{sampleRate / 1000.0:0.#} kHz");

            if (channels == 1)
                details.Add("Mono");
            else if (channels == 2)
                details.Add("Stereo");
            else if (channels > 2)
                details.Add($"{channels} channels");

            if (bitrate > 0)
                details.Add($"{bitrate} kbps");

            return new AudioMetadata(bpmText, lengthText, string.Join(" • ", details));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Metadata unavailable for {fullPath}: {ex.Message}");
            return new AudioMetadata("—", "—", format);
        }
    }

    // signal analysis method 
    public FrequencyAnalysis AnalyzeSignal(string fullPath, CancellationToken cancellationToken)
    {
        const int fftSize = 4096;
        const int fftPower = 12;

        using var reader = new AudioFileReader(fullPath);

        int sampleRate = reader.WaveFormat.SampleRate;
        int channels = reader.WaveFormat.Channels;

        if (sampleRate <= 0 || channels <= 0)
            throw new InvalidDataException("Invalid audio format.");

        // interleaved input and reusable fft buffer 
        float[] samples = new float[fftSize * channels];
        Complex[] spectrum = new Complex[fftSize];

        List<FrequencyFrame> frames = new();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            //fill one analysis frame, unless the file ends.
            int count = 0;

            while (count < samples.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = reader.Read(
                    samples.AsSpan(count, samples.Length - count));
                
                if (read == 0)
                    break;
                
                count += read;
            }

            int availableFrames = count / channels;

            if (availableFrames == 0)
                break;

            double sumSquares = 0;
            double peak = 0;

            //convert multichannel audio to mono for the fft
            for (int i = 0; i < fftSize; i++)
            {
                double mono = 0;

                if(i < availableFrames)
                {
                    for (int channel = 0; channel < channels; channel++)
                    {
                        float value = samples[i * channels + channel];

                        if (!float.IsFinite(value))
                            value = 0;
                        
                        sumSquares += value * (double)value;
                        peak = Math.Max(peak, Math.Abs(value));
                        mono += value;
                    }

                    mono /= channels;

                }

                //han window reduces spectral leakage
                double window = 0.5 - 0.5 * Math.Cos(
                    2 * Math.PI * i / (fftSize - 1));
                
                spectrum[i].X = (float)(mono * window);
                spectrum[i].Y = 0f;
            }

            //loudness measurements in the time domain
            double rms = Math.Sqrt(sumSquares / count);

            //convert samples into frequency domain data
            FastFourierTransform.FFT(
                true, fftPower, spectrum);

            double bassPower = 0;
            double midPower = 0;
            double treblePower = 0;

            //analyze the positive freq half of the FFT
            for (int bin = 1; bin <= fftSize / 2; bin++)
            {
                double frequency =
                    bin * (double)sampleRate / fftSize;

                if (frequency < 20 || frequency >= 16000)
                    continue;
                
                double real = spectrum[bin].X;
                double imaginary = spectrum[bin].Y;

                double power = real * real + imaginary * imaginary;

                if (frequency < 250)
                    bassPower += power;
                else if (frequency < 4000)
                    midPower += power;
                else 
                    treblePower += power;
            }

            double totalPower =
            bassPower + midPower + treblePower;

            // Normalize band energies into relative proportions.
            bool hasEnergy = rms > 0.000001 && totalPower > 0;

            double bass = hasEnergy ? bassPower / totalPower : 0;
            double mid = hasEnergy ? midPower / totalPower : 0;
            double treble = hasEnergy ? treblePower / totalPower : 0;

            frames.Add(new FrequencyFrame(
                bass,
                mid,
                treble,
                ToDbfs(rms),
                ToDbfs(peak)
            ));
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new FrequencyAnalysis(
            fftSize / (double)sampleRate,
            frames.ToArray()
        );
    }


    public TempoEstimate? EstimateTempo(string fullPath, CancellationToken cancellationToken)
    {
        const int fftSize = 2048;
        const int hopSize = 1024;
        const int fftPower = 11; // 2^11 = 2048

        cancellationToken.ThrowIfCancellationRequested();

        // First try the cached result.
        string cachePath = GetTempoCachePath(fullPath);

        if (TryReadTempoCache(cachePath, out TempoEstimate? cached))
            return cached;

        using var reader = new AudioFileReader(fullPath);

        int sampleRate = reader.WaveFormat.SampleRate;
        int channels = reader.WaveFormat.Channels;

        if (sampleRate <= 0 || channels <= 0)
            return null;

        // Keep a sliding window of mono samples.
        float[] windowSamples = new float[fftSize];
        float[] buffer = new float[hopSize * channels];

        Complex[] fft = new Complex[fftSize];
        double[] previousMagnitudes = new double[fftSize / 2];

        List<double> onsetStrength = new();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Shift the older samples to the front.
            Array.Copy(
                windowSamples,
                hopSize,
                windowSamples,
                0,
                fftSize - hopSize);

            int count = 0;

            // Read one hop of interleaved samples.
            while (count < buffer.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = reader.Read(
                    buffer.AsSpan(count, buffer.Length - count));

                if (read == 0)
                    break;

                count += read;
            }

            int framesRead = count / channels;

            if (framesRead == 0)
                break;

            // Convert interleaved channels into mono.
            for (int i = 0; i < hopSize; i++)
            {
                double mono = 0;

                if (i < framesRead)
                {
                    for (int c = 0; c < channels; c++)
                        mono += buffer[i * channels + c];

                    mono /= channels;
                }

                windowSamples[fftSize - hopSize + i] =
                    (float)mono;
            }

            // Apply a Hann window before FFT.
            for (int i = 0; i < fftSize; i++)
            {
                double hann = 0.5 - 0.5 * Math.Cos(
                    2 * Math.PI * i / (fftSize - 1));

                fft[i].X = (float)(windowSamples[i] * hann);
                fft[i].Y = 0;
            }

            FastFourierTransform.FFT(true, fftPower, fft);

            // Spectral flux: measure increases in spectral magnitude.
            double flux = 0;

            for (int bin = 1; bin < fftSize / 2; bin++)
            {
                double frequency =
                    bin * (double)sampleRate / fftSize;

                if (frequency < 40 || frequency > 8000)
                    continue;

                double real = fft[bin].X;
                double imaginary = fft[bin].Y;

                double magnitude = Math.Sqrt(
                    real * real + imaginary * imaginary);

                // Log compression reduces domination by loud bins.
                double compressed = Math.Log(1 + magnitude);

                double increase =
                    compressed - previousMagnitudes[bin];

                if (increase > 0)
                    flux += increase;

                previousMagnitudes[bin] = compressed;
            }

            onsetStrength.Add(flux);
        }

        cancellationToken.ThrowIfCancellationRequested();

        double secondsPerHop = hopSize / (double)sampleRate;

        TempoEstimate? estimate = FindTempo(
            onsetStrength,
            secondsPerHop,
            cancellationToken);

        if (estimate is not null)
            WriteTempoCache(cachePath, estimate);

        return estimate;
    }


    // convert linear amplitude into decibels relative
    private static double ToDbfs(double amplitude)
    {
        return amplitude > 0
            ? 20 * Math.Log10(amplitude)
            : double.NegativeInfinity;
    }

    // Step 2's waveform generator: decode the file once, summarize it into 400 peaks,
    // and cache the result. Each peak covers a roughly equal time interval.
    public float[] GenerateWaveform(string fullPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string cachePath = GetWaveformCachePath(fullPath);
        if (TryReadWaveformCache(cachePath, cancellationToken, out float[]? cached))
            return cached!;

        using var reader = new AudioFileReader(fullPath);
        int channels = reader.WaveFormat.Channels;
        if (channels <= 0)
            throw new InvalidDataException("Audio file contains no channels.");

        // AudioFileReader's output consists of 32-bit floating-point samples.
        double totalFrames = Math.Max(1.0, reader.Length / (sizeof(float) * (double)channels));
        float[] peaks = new float[WaveformBins];
        float[] buffer = new float[8192 * channels];

        long frameIndex = 0;
        int samplesRead;
        while ((samplesRead = reader.Read(buffer.AsSpan())) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Channels are interleaved: L,R,L,R for a stereo recording.
            for (int i = 0; i + channels <= samplesRead; i += channels)
            {
                float amplitude = 0;
                for (int ch = 0; ch < channels; ch++)
                {
                    float sample = buffer[i + ch];
                    if (float.IsFinite(sample))
                        amplitude = Math.Max(amplitude, Math.Abs(sample));
                }

                int bin = Math.Min(WaveformBins - 1,
                    (int)(frameIndex * WaveformBins / totalFrames));
                peaks[bin] = Math.Max(peaks[bin], Math.Min(1f, amplitude));
                frameIndex++;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        TryWriteWaveformCache(cachePath, peaks);
        return peaks;
    }

    private static bool TryReadWaveformCache(
        string cachePath,
        CancellationToken cancellationToken,
        out float[]? peaks)
    {
        peaks = null;
        try
        {
            if (!File.Exists(cachePath))
                return false;

            using var file = new BinaryReader(File.OpenRead(cachePath));
            if (file.BaseStream.Length != WaveformBins * sizeof(float))
                return false;

            float[] values = new float[WaveformBins];
            for (int i = 0; i < values.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                float value = file.ReadSingle();
                if (!float.IsFinite(value) || value < 0 || value > 1)
                    return false;
                values[i] = value;
            }

            peaks = values;
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void TryWriteWaveformCache(string cachePath, float[] peaks)
    {
        // Write to a temporary file so a canceled/crashed decode cannot leave
        // behind a partially written cache that looks valid.
        string tempPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            using (var writer = new BinaryWriter(File.Create(tempPath)))
            {
                foreach (float peak in peaks)
                    writer.Write(peak);
            }

            File.Move(tempPath, cachePath, overwrite: true);
        }
        catch (IOException) { /* Display works even without caching. */ }
        catch (UnauthorizedAccessException) { /* Cache is optional. */ }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string GetWaveformCachePath(string fullPath)
    {
        FileInfo file = new(fullPath);
        string identity = $"{Path.GetFullPath(fullPath).ToUpperInvariant()}|" +
            $"{file.Length}|{file.LastWriteTimeUtc.Ticks}|{WaveformBins}|v1";

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        string cacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MusicPlayer", "Waveforms");
        return Path.Combine(cacheFolder, Convert.ToHexString(hash) + ".bin");
    }
}
