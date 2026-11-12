using Microsoft.Win32;
using Microsoft.VisualBasic;
using System.Net.Http;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;

namespace MusicPlayer;

public partial class MainWindow : Window
{
    private static readonly HttpClient _artworkClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
    private const int MaxArtworkBytes = 5 * 1024 * 1024;

    private readonly AudioPlaybackService _player = new();
    private readonly AudioAnalysisService _analysis = new();
    private readonly Random _random = new();
    private readonly DispatcherTimer _seekTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(50)
    };

    private readonly string _settingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MusicPlayer");

    private List<SongFile> _songs = new();
    private List<SongFile> _visibleSongs = new();
    private string? _currentFolder;
    private SongFile? _currentSong;

    // Step 4: a new selection cancels the previous waveform decode.
    private CancellationTokenSource? _waveformCancellation;
    private float[]? _waveformPeaks;

    private bool _windowReady;
    private bool _ignoreSelectionChanged;
    private bool _mediaReady;
    private bool _isPlaying;
    private bool _playWhenOpened;
    private bool _shuffleEnabled;
    private bool _repeatEnabled;
    private bool _isMuted;
    private bool _updatingSeekSlider;
    private bool _draggingSeekSlider;
    private double _songDurationSeconds;

    public MainWindow()
    {
        InitializeComponent();
        _windowReady = true;

        _player.Volume = VolumeSlider.Value / 100.0;
        UpdateVolumeDisplay();
        UpdatePlayIcon();
        UpdateButtons();
        ResetSeekBar();
        ClearWaveform();

        // Update elapsed time as playback progresses.
        _seekTimer.Tick += SeekTimer_Tick;
        _seekTimer.Start();

        // Thumb drag events are raised by the slider's internal Thumb.
        SeekSlider.AddHandler(Thumb.DragStartedEvent,
            new DragStartedEventHandler(SeekDragStarted), true);
        SeekSlider.AddHandler(Thumb.DragCompletedEvent,
            new DragCompletedEventHandler(SeekDragCompleted), true);

        _player.MediaOpened += (_, _) =>
        {
            _mediaReady = true;
            SetSeekDuration();
            UpdateButtons();

            if (_playWhenOpened)
            {
                StartPlayback();
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
            ResetSeekBar();
            ShowError($"Playback error: {e.ErrorException?.Message ?? "Unknown error"}");
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
                ShowError("Saved default folder no longer exists");
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
                .Select(path => new SongFile(path, _analysis))
                .OrderBy(song => song.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _player.Close();
            _mediaReady = false;
            _isPlaying = false;
            _playWhenOpened = false;
            ClearWaveform();
            _currentSong = null;
            NowPlayingPanel.DataContext = null;
            _currentFolder = folder;
            _songs = files;

            NowPlayingText.Text = "No song selected";
            ResetSeekBar();

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

        // This badge represents the entire opened folder, not search results.
        CountText.Text =
            $"{_songs.Count} {(_songs.Count == 1 ? "song" : "songs")} in current folder";

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

    // ALBUM ARTWORK -------------------------------------------------------

    private void Artwork_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not SongFile song)
            return;

        // Opening this menu should not trigger song playback via a double-click.
        e.Handled = true;

        ContextMenu menu = new();
        MenuItem local = new() { Header = "Choose image from computer..." };
        local.Click += (_, _) => ChooseLocalArtwork(song);

        MenuItem url = new() { Header = "Add image from URL..." };
        url.Click += async (_, _) => await ChooseUrlArtworkAsync(song);

        menu.Items.Add(local);
        menu.Items.Add(url);
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ChooseLocalArtwork(SongFile song)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Select album artwork",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            byte[] bytes = File.ReadAllBytes(dialog.FileName);
            if (bytes.Length > MaxArtworkBytes)
                throw new InvalidDataException("Artwork must be smaller than 5 MB.");

            SaveArtwork(song, bytes);
        }
        catch (Exception ex) when (ex is IOException ||
                                   ex is UnauthorizedAccessException ||
                                   ex is NotSupportedException ||
                                   ex is ArgumentException)
        {
            ShowError($"Could not import artwork: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task ChooseUrlArtworkAsync(SongFile song)
    {
        string input = Interaction.InputBox(
            "Enter a direct HTTPS image URL:", "Album Artwork", "");
        if (string.IsNullOrWhiteSpace(input))
            return;

        if (!Uri.TryCreate(input, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            ShowError("Please enter a valid HTTPS image URL.");
            return;
        }

        try
        {
            using var response = await _artworkClient.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            // Check the final destination when an HTTP redirect occurs.
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Only HTTPS image downloads are allowed.");

            if (response.Content.Headers.ContentLength > MaxArtworkBytes)
                throw new InvalidDataException("Image exceeds the 5 MB limit.");

            string? type = response.Content.Headers.ContentType?.MediaType;
            if (type is not null && !type.StartsWith(
                "image/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The URL did not return an image.");

            await using var inputStream = await response.Content.ReadAsStreamAsync();
            using MemoryStream memory = new();
            byte[] buffer = new byte[8192];
            int count;

            while ((count = await inputStream.ReadAsync(buffer.AsMemory())) > 0)
            {
                if (memory.Length + count > MaxArtworkBytes)
                    throw new InvalidDataException("Image exceeds the 5 MB limit.");
                memory.Write(buffer, 0, count);
            }

            SaveArtwork(song, memory.ToArray());
        }
        catch (Exception ex) when (ex is HttpRequestException ||
                                   ex is System.Threading.Tasks.TaskCanceledException ||
                                   ex is IOException ||
                                   ex is NotSupportedException ||
                                   ex is ArgumentException)
        {
            ShowError($"Could not download artwork: {ex.Message}");
        }
    }

    private static void SaveArtwork(SongFile song, byte[] bytes)
    {
        BitmapImage bitmap = SongFile.DecodeArtwork(bytes);
        string path = SongFile.GetArtworkPath(song.FullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream output = File.Create(path))
            encoder.Save(output);

        song.Artwork = bitmap; // INotifyPropertyChanged refreshes the image.
    }

    // AUDIO PLAYBACK ------------------------------------------------------

    private void OpenSong(SongFile song, bool playImmediately)
    {
        ClearWaveform();
        _currentSong = song;
        NowPlayingPanel.DataContext = song;
        _playWhenOpened = playImmediately;
        _mediaReady = false;
        _isPlaying = false;
        _player.Close();
        ResetSeekBar();

        // Preserve the user's audio preferences when changing tracks.
        _player.Volume = VolumeSlider.Value / 100.0;
        _player.IsMuted = _isMuted;

        NowPlayingText.Text = song.Name;

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

        // Step 4: decode the song asynchronously, without stopping playback.
        _ = LoadWaveformAsync(song);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (!_mediaReady)
            return;

        if (_isPlaying)
        {
            _player.Pause();
            _isPlaying = false;
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
        PreviousButton.IsEnabled = _visibleSongs.Count > 1;
        NextButton.IsEnabled = _visibleSongs.Count > 1;
        ShuffleButton.IsEnabled = _songs.Count > 0;
        RepeatButton.IsEnabled = _songs.Count > 0;
    }

    // WAVEFORM -------------------------------------------------------------

    // Step 4: run slow decoding on a background thread, then return to the
    // WPF UI thread to update the visualization.
    private async Task LoadWaveformAsync(SongFile song)
    {
        var cancellation = new CancellationTokenSource();
        _waveformCancellation = cancellation;
        WaveformStatus.Text = "Generating waveform...";

        try
        {
            float[] peaks = await Task.Run(
                () => _analysis.GenerateWaveform(song.FullPath, cancellation.Token),
                cancellation.Token);

            // A stale result must never replace a newer song's waveform.
            if (cancellation.IsCancellationRequested ||
                !ReferenceEquals(_currentSong, song))
                return;

            _waveformPeaks = peaks;
            WaveformStatus.Text = "";
            DrawWaveform();
            UpdateWaveformPlayhead();
        }
        catch (OperationCanceledException)
        {
            // Expected when changing tracks.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Waveform error: {ex}");
            if (!cancellation.IsCancellationRequested &&
                ReferenceEquals(_currentSong, song))
            {
                WaveformStatus.Text = "Waveform unavailable";
            }
        }
        finally
        {
            if (ReferenceEquals(_waveformCancellation, cancellation))
                _waveformCancellation = null;

            cancellation.Dispose();
        }
    }

    private void ClearWaveform()
    {
        _waveformCancellation?.Cancel();
        _waveformCancellation = null;
        _waveformPeaks = null;
        WaveformPath.Data = null;
        WaveformPlayhead.Visibility = Visibility.Collapsed;
        WaveformStatus.Text = "Select a song to view its waveform";
    }

    // Step 5: make one geometry containing a bar for each waveform bin.
    private void DrawWaveform()
    {
        if (_waveformPeaks is null)
            return;

        double width = WaveformCanvas.ActualWidth;
        double height = WaveformCanvas.ActualHeight;
        if (width <= 1 || height <= 1)
            return;

        double barSpacing = width / _waveformPeaks.Length;
        double barWidth = Math.Max(0.6, barSpacing * 0.7);
        double centerY = height / 2.0;
        double maxHalfHeight = Math.Max(0, centerY - 3);

        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            for (int i = 0; i < _waveformPeaks.Length; i++)
            {
                double amplitude = Math.Max(1.0,
                    _waveformPeaks[i] * maxHalfHeight);
                double x1 = i * barSpacing;
                double x2 = Math.Min(width, x1 + barWidth);
                double top = centerY - amplitude;
                double bottom = centerY + amplitude;

                context.BeginFigure(new Point(x1, top), isFilled: true, isClosed: true);
                context.LineTo(new Point(x2, top), true, false);
                context.LineTo(new Point(x2, bottom), true, false);
                context.LineTo(new Point(x1, bottom), true, false);
            }
        }

        geometry.Freeze();
        WaveformPath.Data = geometry;
    }

    private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawWaveform();
        UpdateWaveformPlayhead();
    }

    // Step 5: the 50ms seek timer already maintains playback position;
    // reuse it instead of starting a separate waveform animation timer.
    private void UpdateWaveformPlayhead()
    {
        if (_waveformPeaks is null || !_mediaReady ||
            _songDurationSeconds <= 0)
        {
            WaveformPlayhead.Visibility = Visibility.Collapsed;
            return;
        }

        double progress = Math.Clamp(
            _player.Position.TotalSeconds / _songDurationSeconds, 0.0, 1.0);
        double x = progress * WaveformCanvas.ActualWidth;

        WaveformPlayhead.X1 = x;
        WaveformPlayhead.X2 = x;
        WaveformPlayhead.Y1 = 0;
        WaveformPlayhead.Y2 = WaveformCanvas.ActualHeight;
        WaveformPlayhead.Visibility = Visibility.Visible;
    }

    // PLAYBACK TIMELINE ---------------------------------------------------

    private void SetSeekDuration()
    {
        TimeSpan? duration = _player.Duration;
        if (duration is null)
        {
            ResetSeekBar();
            return;
        }

        _songDurationSeconds = duration.Value.TotalSeconds;
        SeekSlider.Maximum = Math.Max(1, _songDurationSeconds);
        SeekSlider.IsEnabled = _songDurationSeconds > 0;
        DurationText.Text = FormatTime(duration.Value);
        UpdateSeekPosition();
    }

    private void ResetSeekBar()
    {
        _songDurationSeconds = 0;
        _updatingSeekSlider = true;
        SeekSlider.IsEnabled = false;
        SeekSlider.Maximum = 1;
        SeekSlider.Value = 0;
        _updatingSeekSlider = false;
        ElapsedTimeText.Text = "0:00";
        DurationText.Text = "0:00";
    }

    private void SeekTimer_Tick(object? sender, EventArgs e)
    {
        if (_mediaReady && !_draggingSeekSlider)
            UpdateSeekPosition();
    }

    private void UpdateSeekPosition()
    {
        if (!_mediaReady || _songDurationSeconds <= 0)
            return;

        _updatingSeekSlider = true;
        SeekSlider.Value = Math.Clamp(_player.Position.TotalSeconds,
            0, _songDurationSeconds);
        _updatingSeekSlider = false;
        ElapsedTimeText.Text = FormatTime(_player.Position);
        UpdateWaveformPlayhead();
    }

    private void SeekSlider_ValueChanged(
        object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Ignore initialization and timer-driven slider changes.
        if (!_windowReady || _updatingSeekSlider || !_mediaReady)
            return;

        ElapsedTimeText.Text = FormatTime(TimeSpan.FromSeconds(e.NewValue));
        if (!_draggingSeekSlider)
            SeekTo(e.NewValue); // Clicking the track seeks immediately.
    }

    private void SeekDragStarted(object sender, DragStartedEventArgs e)
    {
        _draggingSeekSlider = true;
    }

    private void SeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _draggingSeekSlider = false;
        if (_mediaReady)
            SeekTo(SeekSlider.Value);
    }

    private void SeekTo(double seconds)
    {
        if (!_mediaReady || _songDurationSeconds <= 0)
            return;

        _player.Position = TimeSpan.FromSeconds(
            Math.Clamp(seconds, 0, _songDurationSeconds));
        UpdateSeekPosition();
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");

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
        _waveformCancellation?.Cancel();
        _seekTimer.Stop();
        _player.Close();
        base.OnClosed(e);
    }
}
