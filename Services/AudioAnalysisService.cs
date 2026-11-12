using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NAudio.Wave;

namespace MusicPlayer;

// A simple immutable group of metadata values consumed by SongFile.
public sealed record AudioMetadata(
    string BpmText,
    string LengthText,
    string AudioDetails);

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
