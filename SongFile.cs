using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.ComponentModel;
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
    public string TempoInfoText { get; private set; } = "";

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

    public SongFile(string fullPath, AudioAnalysisService analysis)
    {
        FullPath = fullPath;

        // Step 2: data model asks the analysis service to extract metadata.
        AudioMetadata metadata = analysis.ReadMetadata(fullPath);
        BpmText = metadata.BpmText;
        LengthText = metadata.LengthText;
        AudioDetails = metadata.AudioDetails;
        TempoInfoText = BpmText == "—"
            ? "BPM not analyzed"
            : "BPM from file metadata";

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

    public void ApplyTempoEstimate(TempoEstimate? estimate)
    {
        // Never overwrite an existing BPM metadata value.
        if (TempoInfoText == "BPM from file metadata")
            return;

        if (estimate is null)
        {
            TempoInfoText = "Unable to estimate BPM";
        }
        else
        {
            BpmText = $"{estimate.Bpm:0}";

            TempoInfoText =
                $"Estimated BPM · Rhythm strength: " +
                $"{estimate.RhythmStrength:P0}";

            // Tell WPF the BPM value has changed.
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(BpmText)));
        }

        // Tell WPF the source information has changed.
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(TempoInfoText)));
    }

}
