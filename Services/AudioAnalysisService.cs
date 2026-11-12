using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace MusicPlayer;

// A container for information extracted from an audio file.
public sealed record AudioMetadata(
    string BpmText,
    string LengthText,
    string AudioDetails
);

public sealed class AudioAnalysisService
{
    public AudioMetadata ReadMetadata(string fullPath)
    {
        string format = Path.GetExtension(fullPath)
            .TrimStart('.')
            .ToUpperInvariant();

        try
        {
            using var file = TagLib.File.Create(fullPath);

            // Duration
            TimeSpan duration = file.Properties.Duration;

            string lengthText = duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss")
                : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

            // BPM stored in metadata
            uint bpm = file.Tag.BeatsPerMinute;
            string bpmText = bpm > 0 ? bpm.ToString() : "—";

            // Technical audio information
            int sampleRate = file.Properties.AudioSampleRate;
            int channels = file.Properties.AudioChannels;
            int bitrate = file.Properties.AudioBitrate;

            List<string> details = new() { format };

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

            return new AudioMetadata(
                bpmText,
                lengthText,
                string.Join(" • ", details)
            );
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Metadata unavailable for {fullPath}: {ex.Message}");

            // Preserve basic information even when metadata fails.
            return new AudioMetadata("—", "—", format);
        }
    }
}
