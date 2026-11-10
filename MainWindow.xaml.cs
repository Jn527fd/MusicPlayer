
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MusicPlayer;

public partial class MainWindow : Window
{
    // ==========================
    // FIELDS
    // ==========================

    // Audio playback object
    private readonly MediaPlayer _player = new();

    // Directory for application settings
    private readonly string _settingsDirectory = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "MusicPlayer");

    // Current music folder
    private string? _currentFolder;

    // Tracks whether audio is ready
    private bool _mediaReady = false;


    // ==========================
    // CONSTRUCTOR
    // ==========================

    public MainWindow()
    {
        InitializeComponent();

        // Initial volume: 50%
        _player.Volume = 0.5;
        UpdateVolumeText();

        // Media opened successfully
        _player.MediaOpened += (_, _) =>
        {
            _mediaReady = true;

            SetPlaybackButtons(true);

            StatusText.Text = "Ready to play";
        };

        // Song reached the end
        _player.MediaEnded += (_, _) =>
        {
            _player.Stop();

            StatusText.Text = "Song finished";
        };

        // Failed to open or play audio
        _player.MediaFailed += (_, e) =>
        {
            _mediaReady = false;

            SetPlaybackButtons(false);

            StatusText.Text =
                $"Playback error: {e.ErrorException.Message}";
        };

        // Load saved music folder on startup
        LoadDefaultFolder();
    }


    // ==========================
    // FILE MENU
    // ==========================

    // Open one file and scan its folder
    private void OpenFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Select a song",
            Filter = "Audio Files|*.mp3;*.wav"
        };

        if (Directory.Exists(_currentFolder))
        {
            dialog.InitialDirectory = _currentFolder;
        }

        if (dialog.ShowDialog(this) != true)
            return;

        string? folder =
            Path.GetDirectoryName(dialog.FileName);

        if (folder != null)
        {
            LoadFolder(folder, dialog.FileName);
        }
    }


    // Open folder containing music
    private void OpenFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Select music folder"
        };

        if (dialog.ShowDialog(this) == true)
        {
            LoadFolder(dialog.FolderName);
        }
    }


    // Save the default music folder
    private void SetDefaultFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Select default music folder"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        // Load and validate folder
        if (!LoadFolder(dialog.FolderName))
            return;

        try
        {
            // Ensure settings directory exists
            Directory.CreateDirectory(
                _settingsDirectory);

            string settingsFile = Path.Combine(
                _settingsDirectory,
                "default-folder.txt");

            // Save selected folder path
            File.WriteAllText(
                settingsFile,
                dialog.FolderName);

            StatusText.Text = "Default folder saved";
        }
        catch (IOException ex)
        {
            ShowError(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowError(ex.Message);
        }
    }


    // Remove saved default folder
    private void ClearDefaultFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        string settingsFile = Path.Combine(
            _settingsDirectory,
            "default-folder.txt");

        try
        {
            if (File.Exists(settingsFile))
            {
                File.Delete(settingsFile);
            }

            StatusText.Text = "Default folder cleared";
        }
        catch (IOException ex)
        {
            ShowError(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowError(ex.Message);
        }
    }


    // ==========================
    // MUSIC LIBRARY
    // ==========================

    // Load the saved default folder
    private void LoadDefaultFolder()
    {
        string settingsFile = Path.Combine(
            _settingsDirectory,
            "default-folder.txt");

        if (!File.Exists(settingsFile))
            return;

        try
        {
            string folder =
                File.ReadAllText(settingsFile);

            if (Directory.Exists(folder))
            {
                LoadFolder(folder);
            }
            else
            {
                StatusText.Text =
                    "Default folder not found";
            }
        }
        catch (IOException ex)
        {
            ShowError(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowError(ex.Message);
        }
    }


    // Find MP3/WAV files and populate playlist
    private bool LoadFolder(
        string folder,
        string? selectedFile = null)
    {
        try
        {
            var songs = Directory
                .EnumerateFiles(
                    folder,
                    "*",
                    SearchOption.TopDirectoryOnly)

                // Keep supported audio formats
                .Where(file =>
                    Path.GetExtension(file).Equals(
                        ".mp3",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    Path.GetExtension(file).Equals(
                        ".wav",
                        StringComparison.OrdinalIgnoreCase))

                // Convert paths into SongFile objects
                .Select(file => new SongFile(file))

                // Sort alphabetically
                .OrderBy(song => song.Name)

                // Convert to a list
                .ToList();


            // Update current folder
            _currentFolder = folder;

            // Update interface
            FolderText.Text = folder;
            CountText.Text = $"{songs.Count} songs";

            // Reset the previous audio
            _player.Close();

            _mediaReady = false;

            SetPlaybackButtons(false);

            NowPlayingText.Text =
                "No song selected";

            // Populate the playlist
            SongList.ItemsSource = songs;

            // Select requested song or first available
            SongList.SelectedItem =
                songs.FirstOrDefault(song =>
                    string.Equals(
                        song.FullPath,
                        selectedFile,
                        StringComparison.OrdinalIgnoreCase))
                ?? songs.FirstOrDefault();

            if (songs.Count == 0)
            {
                StatusText.Text =
                    "No MP3 or WAV files found";
            }

            return true;
        }
        catch (IOException ex)
        {
            ShowError(ex.Message);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowError(ex.Message);
            return false;
        }
    }


    // Called when the selected song changes
    private void SongList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (SongList.SelectedItem is not SongFile song)
            return;

        // Stop and close previous media
        _player.Close();

        _mediaReady = false;

        SetPlaybackButtons(false);

        // Update now-playing label
        NowPlayingText.Text = song.Name;

        StatusText.Text = "Loading...";

        // Open selected audio file
        _player.Open(
            new Uri(
                song.FullPath,
                UriKind.Absolute));
    }


    // ==========================
    // PLAYBACK CONTROLS
    // ==========================

    private void Play_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_mediaReady)
            return;

        _player.Play();

        StatusText.Text = "Playing";
    }


    private void Pause_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_mediaReady)
            return;

        _player.Pause();

        StatusText.Text = "Paused";
    }


    private void Stop_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_mediaReady)
            return;

        _player.Stop();

        StatusText.Text = "Stopped";
    }


    // Enable or disable playback buttons
    private void SetPlaybackButtons(bool enabled)
    {
        PlayButton.IsEnabled = enabled;
        PauseButton.IsEnabled = enabled;
        StopButton.IsEnabled = enabled;
    }


    // ==========================
    // VOLUME CONTROLS
    // ==========================

    // Called when the volume slider changes
    private void VolumeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        // Prevent initialization errors
        if (VolumeText is null)
            return;

        // Convert 0-100 slider range to 0.0-1.0
        _player.Volume = e.NewValue / 100.0;

        UpdateVolumeText();
    }


    // Toggle mute/unmute
    private void Mute_Click(
        object sender,
        RoutedEventArgs e)
    {
        _player.IsMuted = !_player.IsMuted;

        MuteButton.Content = _player.IsMuted
            ? "Unmute"
            : "Mute";

        UpdateVolumeText();
    }


    // Update displayed volume
    private void UpdateVolumeText()
    {
        VolumeText.Text = _player.IsMuted
            ? "Volume: Muted"
            : $"Volume: {_player.Volume:P0}";
    }


    // ==========================
    // SOUND MENU
    // ==========================

    // Future equalizer feature
    private void Equalizer_Click(
        object sender,
        RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Equalizer settings will be implemented "
            + "in a future version.",
            "MusicPlayer Equalizer",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }


    // ==========================
    // ERROR HANDLING
    // ==========================

    private void ShowError(string message)
    {
        MessageBox.Show(
            this,
            message,
            "MusicPlayer",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }


    // ==========================
    // APPLICATION CLEANUP
    // ==========================

    protected override void OnClosed(EventArgs e)
    {
        _player.Close();

        base.OnClosed(e);
    }
}


// ==========================
// SONG MODEL
// ==========================

// Represents one music file in the playlist
public class SongFile
{
    // Absolute path to the music file
    public string FullPath { get; }

    // Filename without directory path
    public string Name =>
        Path.GetFileName(FullPath);

    // Constructor
    public SongFile(string fullPath)
    {
        FullPath = fullPath;
    }
}
