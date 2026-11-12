using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.ComponentModel;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace MusicPlayer;

// Data associated with a single audio file in the music library.
public class SongFile : INotifyPropertyChanged
{
    public string FullPath { get; }
    public string Name => Path.GetFileName(FullPath);
    public string BpmText { get; private set; } = "—";
    public string LengthText { get; private set; } = "—";
    public string AudioDetails { get; private set; } = "Unavailable";

    private BitmapImage? _artwork;
    public BitmapImage? Artwork
    {
        get => _artwork;
        set
        {
            _artwork = value;
            PropertyChanged?.Invoke(this,
                new PropertyChangedEventArgs(nameof(Artwork)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SongFile(string fullPath)
    {
        FullPath = fullPath;

        AudioDetails = Path.GetExtension(fullPath)
            .TrimStart('.')
            .ToUpperInvariant();

        try
        {
            using var file = TagLib.File.Create(fullPath);
            TimeSpan duration = file.Properties.Duration;
            LengthText = duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss")
                : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

            uint bpm = file.Tag.BeatsPerMinute;
            BpmText = bpm > 0 ? bpm.ToString() : "—";

            int sampleRate = file.Properties.AudioSampleRate;
            int channels = file.Properties.AudioChannels;
            int bitrate = file.Properties.AudioBitrate;

            // begin with the audio format
            List<string> details = new() { AudioDetails };

            // add sample rate when avaliable 
            if (sampleRate > 0)
            {
                details.Add($"{sampleRate / 1000.0:0.#} kHz");
            }

            // add channel information
            if (channels == 1)
                details.Add("Mono");
            else if (channels == 2)
                details.Add("Stereo");
            else if (channels > 2)
                details.Add($"{channels} channels");
            
            if (bitrate > 0)
            {
                details.Add($"{bitrate} kbps");
            }

            AudioDetails = string.Join(" • ", details);

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Metadata unavailable for {fullPath}: {ex.Message}");
        }

        string imagePath = GetArtworkPath(fullPath);
        if (File.Exists(imagePath))
        {
            try
            {
                Artwork = DecodeArtwork(File.ReadAllBytes(imagePath));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Artwork unavailable for {fullPath}: {ex.Message}");
            }
        }
    }

    public static string GetArtworkPath(string songPath)
    {
        string normalized = Path.GetFullPath(songPath).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MusicPlayer", "Artwork");

        return Path.Combine(folder, Convert.ToHexString(hash) + ".png");
    }

    public static BitmapImage DecodeArtwork(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 256;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
