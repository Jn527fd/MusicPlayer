using System;
using System.Windows.Media;

namespace MusicPlayer;

// Isolates the WPF MediaPlayer from the window's UI and navigation code.
// The waveform analyzer opens a separate decoder and never changes playback.
public sealed class AudioPlaybackService
{
    private readonly MediaPlayer _mediaPlayer = new();

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

    public TimeSpan Position
    {
        get => _mediaPlayer.Position;
        set => _mediaPlayer.Position = value;
    }

    public TimeSpan? Duration => _mediaPlayer.NaturalDuration.HasTimeSpan
        ? _mediaPlayer.NaturalDuration.TimeSpan
        : null;

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

    public void Open(Uri uri) => _mediaPlayer.Open(uri);
    public void Play() => _mediaPlayer.Play();
    public void Pause() => _mediaPlayer.Pause();
    public void Stop() => _mediaPlayer.Stop();
    public void Close() => _mediaPlayer.Close();
}
