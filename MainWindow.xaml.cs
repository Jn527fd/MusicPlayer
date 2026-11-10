using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MusicPlayer;

public partial class MainWindow : Window
{
    private readonly MediaPlayer _player = new();
    private readonly Random _random = new();

    private readonly string _settingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MusicPlayer");

    private List<SongFile> _songs = new();
    private List<SongFile> _visibleSongs = new();
    private string? _currentFolder;
    private SongFile? _currentSong;

    private bool _windowReady;
    private bool _ignoreSelectionChanged;
    private bool _mediaReady;
    private bool _isPlaying;
    private bool _playWhenOpened;
    private bool _shuffleEnabled;
    private bool _repeatEnabled;
    private bool _isMuted;

    public MainWindow()
    {
        InitializeComponent();
        _windowReady = true;

        _player.Volume = VolumeSlider.Value / 100.0;
        UpdateVolumeDisplay();
        UpdatePlayIcon();
        UpdateButtons();

        _player.MediaOpened += (_, _) =>
        {
            _mediaReady = true;
            UpdateButtons();

            if (_playWhenOpened)
            {
                StartPlayback();
            }
            else
            {
                StatusText.Text = "Ready to play";
            }
        };

        _player.MediaEnded += (_, _) =>
        {
            if (_repeatEnabled && _currentSong != null)
            {
                _player.Position = TimeSpan.Zero;
                StartPlayback();
            }
            else
            {
                MoveSong(1, fromSongEnd: true);
            }
        };

        _player.MediaFailed += (_, e) =>
        {
            _mediaReady = false;
            _isPlaying = false;
            _playWhenOpened = false;
            StatusText.Text = $"Playback error: {e.ErrorException?.Message ?? "Unknown error"}";
            UpdatePlayIcon();
            UpdateButtons();
        };

        LoadDefaultFolder();
    }

    // FILE MENU -----------------------------------------------------------

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Select a song",
            Filter = "Audio files|*.mp3;*.wav"
        };

        if (Directory.Exists(_currentFolder))
            dialog.InitialDirectory = _currentFolder;

        if (dialog.ShowDialog(this) != true)
            return;

        string? folder = Path.GetDirectoryName(dialog.FileName);
        if (folder != null)
            LoadFolder(folder, dialog.FileName);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new() { Title = "Select music folder" };
        if (dialog.ShowDialog(this) == true)
            LoadFolder(dialog.FolderName);
    }

    private void SetDefaultFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new() { Title = "Select default music folder" };
        if (dialog.ShowDialog(this) != true || !LoadFolder(dialog.FolderName))
            return;

        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            File.WriteAllText(DefaultFolderSettingsFile, dialog.FolderName);
            StatusText.Text = "Default folder saved";
        }
        catch (IOException ex) { ShowError(ex.Message); }
        catch (UnauthorizedAccessException ex) { ShowError(ex.Message); }
    }

    private void ClearDefaultFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(DefaultFolderSettingsFile))
                File.Delete(DefaultFolderSettingsFile);

            StatusText.Text = "Default folder cleared (current playlist kept)";
        }
        catch (IOException ex) { ShowError(ex.Message); }
        catch (UnauthorizedAccessException ex) { ShowError(ex.Message); }
    }

    private string DefaultFolderSettingsFile =>
        Path.Combine(_settingsDirectory, "default-folder.txt");

    private void LoadDefaultFolder()
    {
        try
        {
            if (!File.Exists(DefaultFolderSettingsFile))
                return;

            string folder = File.ReadAllText(DefaultFolderSettingsFile);
            if (Directory.Exists(folder))
                LoadFolder(folder);
            else
                StatusText.Text = "Saved default folder no longer exists";
        }
        catch (IOException ex) { ShowError(ex.Message); }
        catch (UnauthorizedAccessException ex) { ShowError(ex.Message); }
    }

    // MUSIC LIBRARY AND SEARCH -------------------------------------------

    private bool LoadFolder(string folder, string? selectedFile = null)
    {
        try
        {
            List<SongFile> files = Directory
                .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(path =>
                    string.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
                .Select(path => new SongFile(path))
                .OrderBy(song => song.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _player.Close();
            _mediaReady = false;
            _isPlaying = false;
            _playWhenOpened = false;
            _currentSong = null;
            _currentFolder = folder;
            _songs = files;

            FolderText.Text = folder;
            NowPlayingText.Text = "No song selected";
            StatusText.Text = files.Count == 0
                ? "No MP3 or WAV files in this folder"
                : "Select a song and press Play";

            // Clear any old search when switching folders.
            SearchBox.Clear();
            RefreshSongList();

            SongFile? firstSong = files.FirstOrDefault(song =>
                string.Equals(song.FullPath, selectedFile,
                    StringComparison.OrdinalIgnoreCase)) ?? files.FirstOrDefault();

            if (firstSong != null)
                SongList.SelectedItem = firstSong;

            UpdatePlayIcon();
            UpdateButtons();
            return true;
        }
        catch (IOException ex) { ShowError(ex.Message); }
        catch (UnauthorizedAccessException ex) { ShowError(ex.Message); }

        return false;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // XAML can raise this event while InitializeComponent is running.
        if (!_windowReady)
            return;

        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;

        RefreshSongList();
    }

    private void RefreshSongList()
    {
        string query = SearchBox.Text.Trim();
        SongFile? previousSelection = SongList.SelectedItem as SongFile;

        _visibleSongs = _songs
            .Where(song => song.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _ignoreSelectionChanged = true;
        SongList.ItemsSource = _visibleSongs;
        if (previousSelection != null && _visibleSongs.Contains(previousSelection))
            SongList.SelectedItem = previousSelection;
        else if (_currentSong != null && _visibleSongs.Contains(_currentSong))
            SongList.SelectedItem = _currentSong;
        _ignoreSelectionChanged = false;

        CountText.Text = query.Length == 0
            ? $"{_songs.Count} songs"
            : $"{_visibleSongs.Count} of {_songs.Count} songs";

        UpdateButtons();
    }

    private void SongList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreSelectionChanged || SongList.SelectedItem is not SongFile song)
            return;

        if (_currentSong != song)
            OpenSong(song, playImmediately: false);
    }

    private void SongList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SongList.SelectedItem is not SongFile song)
            return;

        if (_currentSong == song)
        {
            _playWhenOpened = true;
            if (_mediaReady)
                StartPlayback();
        }
        else
        {
            OpenSong(song, playImmediately: true);
        }
    }

    // AUDIO PLAYBACK ------------------------------------------------------

    private void OpenSong(SongFile song, bool playImmediately)
    {
        _currentSong = song;
        _playWhenOpened = playImmediately;
        _mediaReady = false;
        _isPlaying = false;
        _player.Close();

        // Preserve the user's audio preferences when changing tracks.
        _player.Volume = VolumeSlider.Value / 100.0;
        _player.IsMuted = _isMuted;

        NowPlayingText.Text = song.Name;
        StatusText.Text = "Loading...";

        // Keep the playlist selection in sync without reopening the song.
        if (_visibleSongs.Contains(song) && SongList.SelectedItem != song)
        {
            _ignoreSelectionChanged = true;
            SongList.SelectedItem = song;
            _ignoreSelectionChanged = false;
        }

        UpdatePlayIcon();
        UpdateButtons();
        _player.Open(new Uri(song.FullPath, UriKind.Absolute));
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (!_mediaReady)
            return;

        if (_isPlaying)
        {
            _player.Pause();
            _isPlaying = false;
            StatusText.Text = "Paused";
            UpdatePlayIcon();
        }
        else
        {
            StartPlayback();
        }
    }

    private void StartPlayback()
    {
        if (!_mediaReady)
            return;

        _player.Play();
        _isPlaying = true;
        _playWhenOpened = false;
        StatusText.Text = "Playing";
        UpdatePlayIcon();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (!_mediaReady)
            return;

        _player.Stop();
        _isPlaying = false;
        StatusText.Text = "Stopped";
        UpdatePlayIcon();
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => MoveSong(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => MoveSong(1);

    private void MoveSong(int direction, bool fromSongEnd = false)
    {
        // When searching, previous/next navigate the visible results.
        if (_visibleSongs.Count == 0)
        {
            FinishPlayback();
            return;
        }

        int currentIndex = _currentSong == null
            ? -1 : _visibleSongs.IndexOf(_currentSong);
        int nextIndex;

        if (_shuffleEnabled && _visibleSongs.Count > 1)
        {
            do { nextIndex = _random.Next(_visibleSongs.Count); }
            while (nextIndex == currentIndex);
        }
        else
        {
            // Automatic playback ends after the last song unless Repeat is on.
            if (fromSongEnd && currentIndex == _visibleSongs.Count - 1)
            {
                FinishPlayback();
                return;
            }

            nextIndex = currentIndex < 0
                ? (direction > 0 ? 0 : _visibleSongs.Count - 1)
                : (currentIndex + direction + _visibleSongs.Count) % _visibleSongs.Count;
        }

        OpenSong(_visibleSongs[nextIndex], playImmediately: true);
    }

    private void FinishPlayback()
    {
        _player.Stop();
        _isPlaying = false;
        StatusText.Text = "Playlist finished";
        UpdatePlayIcon();
    }

    private void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        _shuffleEnabled = !_shuffleEnabled;
        ShuffleButton.Background = _shuffleEnabled ? ActiveButtonBrush : InactiveButtonBrush;
        ShuffleButton.ToolTip = _shuffleEnabled ? "Shuffle: On" : "Shuffle: Off";
    }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        _repeatEnabled = !_repeatEnabled;
        RepeatButton.Background = _repeatEnabled ? ActiveButtonBrush : InactiveButtonBrush;
        RepeatButton.ToolTip = _repeatEnabled
            ? "Repeat current song: On" : "Repeat current song: Off";
    }

    private static readonly Brush ActiveButtonBrush =
        new SolidColorBrush(Color.FromRgb(57, 127, 189));
    private static readonly Brush InactiveButtonBrush =
        new SolidColorBrush(Color.FromRgb(45, 53, 69));

    private void UpdatePlayIcon()
    {
        PlayPauseIcon.Data = Geometry.Parse(_isPlaying
            ? "M7,5 L11,5 L11,19 L7,19 Z M15,5 L19,5 L19,19 L15,19 Z"
            : "M8,5 L20,12 L8,19 Z");
        PlayPauseButton.ToolTip = _isPlaying ? "Pause" : "Play";
    }

    private void UpdateButtons()
    {
        PlayPauseButton.IsEnabled = _mediaReady;
        StopButton.IsEnabled = _mediaReady;
        PreviousButton.IsEnabled = _visibleSongs.Count > 1;
        NextButton.IsEnabled = _visibleSongs.Count > 1;
        ShuffleButton.IsEnabled = _songs.Count > 0;
        RepeatButton.IsEnabled = _songs.Count > 0;
    }

    // VOLUME --------------------------------------------------------------

    private void VolumeSlider_ValueChanged(
        object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_windowReady)
            return;

        _player.Volume = e.NewValue / 100.0;
        UpdateVolumeDisplay();
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _isMuted = !_isMuted;
        _player.IsMuted = _isMuted;
        UpdateVolumeDisplay();
    }

    private void UpdateVolumeDisplay()
    {
        VolumeText.Text = $"{VolumeSlider.Value:0}%";
        MuteButton.ToolTip = _isMuted ? "Unmute" : "Mute";
        SoundWavePath.Data = Geometry.Parse(_isMuted
            ? "M15,9 L21,17 M21,9 L15,17"
            : "M15,9 C18,10 18,14 15,15 M17,6 C23,9 23,15 17,18");
    }

    // MENUS AND CLEANUP ---------------------------------------------------

    private void Equalizer_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "The equalizer and saved EQ presets will be added in a later version.",
            "MusicPlayer Equalizer", MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "MusicPlayer — a C# / .NET WPF music player prototype.",
            "About MusicPlayer", MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "MusicPlayer",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void OnClosed(EventArgs e)
    {
        _player.Close();
        base.OnClosed(e);
    }
}

// Represents one music file shown in the playlist.
public class SongFile
{
    public string FullPath { get; }
    public string Name => Path.GetFileName(FullPath);

    public SongFile(string fullPath)
    {
        FullPath = fullPath;
    }
}
