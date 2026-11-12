using System;
using System.Windows.Media;

namespace MusicPlayer;

public sealed class AudioPlaybackService
{
    // The actual WPF audio player.
    private readonly MediaPlayer _mediaPlayer = new();

    // Forward media events to other classes.
    public event EventHandler MediaOpened
    {
        add => _mediaPlayer.MediaOpened += value;
        remove => _mediaPlayer.MediaOpened -= value;
    }

    public event EventHandler MediaEnded
    {
        add => _mediaPlayer.MediaEnded += value;
        remove => _mediaPlayer.MediaEnded -= value;
    }

    public event EventHandler<ExceptionEventArgs> MediaFailed
    {
        add => _mediaPlayer.MediaFailed += value;
        remove => _mediaPlayer.MediaFailed -= value;
    }

    // Current playback position.
    public TimeSpan Position
    {
        get => _mediaPlayer.Position;
        set => _mediaPlayer.Position = value;
    }

    // Song duration, if available.
    public TimeSpan? Duration =>
        _mediaPlayer.NaturalDuration.HasTimeSpan
            ? _mediaPlayer.NaturalDuration.TimeSpan
            : null;

    // Volume ranges from 0.0 to 1.0.
    public double Volume
    {
        get => _mediaPlayer.Volume;
        set => _mediaPlayer.Volume = value;
    }

    public bool IsMuted
    {
        get => _mediaPlayer.IsMuted;
        set => _mediaPlayer.IsMuted = value;
    }

    // Playback operations.
    public void Open(Uri uri) => _mediaPlayer.Open(uri);

    public void Play() => _mediaPlayer.Play();

    public void Pause() => _mediaPlayer.Pause();

    public void Stop() => _mediaPlayer.Stop();

    public void Close() => _mediaPlayer.Close();
}
