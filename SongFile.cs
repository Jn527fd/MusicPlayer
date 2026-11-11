using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace MusicPlayer;

public class SongFile : INotifyPropertyChanged
{
    public string FullPath { get; }
    public string Name => Path.GetFileName(FullPath);
    public string BpmText { get; private set; } = "__";
    public string LengthText { get; private set; } = "__";
    private BitmapImage? _artwork;
    
    public BitmapImage? Artwork 
    {
        get => _artwork;

        set
        {
           _artwork = value;

           PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Artwork)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SongFile(string fullPath)
    {
        FullPath = fullPath;

        // reads audio metadata
        try
        {
            using var file = TagLib.File.Create(fullPath);
            TimeSpan duration = file.Properties.Duration;

            LengthText = duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss")
                : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

            uint bpm = file.Tag.BeatsPerMinute;

            BpmText = bpm > 0 ? bpm.ToString() : "__";
        }
        catch (Exception ex)
        {
            // bad metadata should not prevent a song 
            // from appearing in the library
            Debug.WriteLine(
                $"Could not read metadata from {fullPath}: {ex.Message}");
        }

        // restore artwork saved during an earlier session 
        string artworkPath = GetArtworkPath(fullPath);

        if (File.Exists(artworkPath))
        {
            try
            {
                Artwork = DecodeArtwork(
                    File.ReadAllBytes(artworkPath));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"COuld not load artwork: {ex.Message}");
            }
        }
    }

    // generate a stable filename for this songs artwork
    public static string GetArtworkPath(string songPath) 
    {
        string normalized = Path.GetFullPath(songPath).ToUpperInvariant();

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        string folder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
                "MusicPlayer",
                "Artwork");
        
        return Path.Combine(
            folder,
            Convert.ToHexString(hash) + ".png");
    }

    // convert image bytes into a wpf image 
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