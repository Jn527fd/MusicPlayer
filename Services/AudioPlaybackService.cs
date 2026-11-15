using System;
using System.Windows;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MusicPlayer;

// Preserves the error property used by MainWindow.
public sealed class PlaybackErrorEventArgs : EventArgs
{
    public Exception ErrorException { get; }

    public PlaybackErrorEventArgs(Exception exception)
    {
        ErrorException = exception;
    }
}

public sealed class AudioPlaybackService
{
    private WaveOut? _output;
    private AudioFileReader? _reader;
    private EqualizerSampleProvider? _equalizer;

    private double _volume = 1.0;
    private bool _isMuted;
    private bool _eqEnabled = true;

    private float _bass;
    private float _mid;
    private float _treble;

    public event EventHandler? MediaOpened;
    public event EventHandler? MediaEnded;
    public event EventHandler<PlaybackErrorEventArgs>? MediaFailed;

    public TimeSpan Position
    {
        get => _reader?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (_reader != null)
                _reader.CurrentTime = value;
        }
    }

    public TimeSpan? Duration => _reader?.TotalTime;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0, 1.0);
            ApplyVolume();
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            ApplyVolume();
        }
    }

    public float BassGain => _bass;
    public float MidGain => _mid;
    public float TrebleGain => _treble;
    public bool EqualizerEnabled => _eqEnabled;

    private void ApplyVolume()
    {
        if (_reader != null)
            _reader.Volume = _isMuted ? 0f : (float)_volume;
    }

    public void SetEqualizer(
        float bass, float mid, float treble)
    {
        _bass = Math.Clamp(bass, -9f, 9f);
        _mid = Math.Clamp(mid, -9f, 9f);
        _treble = Math.Clamp(treble, -9f, 9f);

        _equalizer?.SetGains(_bass, _mid, _treble);
    }

    public void SetEqualizerEnabled(bool enabled)
    {
        _eqEnabled = enabled;
        _equalizer?.SetEnabled(enabled);
    }

    public void Open(Uri uri)
    {
        Close();

        try
        {
            AudioFileReader reader = new(uri.LocalPath);
            EqualizerSampleProvider equalizer = new(reader);

            equalizer.SetGains(_bass, _mid, _treble);
            equalizer.SetEnabled(_eqEnabled);

            WaveOut output = new();

            try
            {
                // Connect: decoder -> equalizer -> audio output.
                output.Init(equalizer.ToWaveProvider());
            }
            catch
            {
                output.Dispose();
                reader.Dispose();
                throw;
            }

            _reader = reader;
            _equalizer = equalizer;
            _output = output;

            ApplyVolume();

            output.PlaybackStopped += (_, args) =>
            {
                // Ignore completion events from an old track.
                if (!ReferenceEquals(_output, output))
                {
                    output.Dispose();
                    reader.Dispose();
                    return;
                }

                // Return to the WPF UI thread before invoking events.
                Application.Current?.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (!ReferenceEquals(_output, output))
                            return;

                        if (args.Exception != null)
                        {
                            MediaFailed?.Invoke(
                                this,
                                new PlaybackErrorEventArgs(
                                    args.Exception));
                        }
                        else if (reader.Position >= reader.Length)
                        {
                            MediaEnded?.Invoke(this, EventArgs.Empty);
                        }
                    }));
            };

            // Notify MainWindow that the file is ready.
            MediaOpened?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MediaFailed?.Invoke(
                this,
                new PlaybackErrorEventArgs(ex));
        }
    }

    public void Play() => _output?.Play();

    public void Pause() => _output?.Pause();

    public void Stop() => _output?.Stop();

    public void Close()
    {
        WaveOut? oldOutput = _output;
        AudioFileReader? oldReader = _reader;

        _output = null;
        _reader = null;
        _equalizer = null;

        if (oldOutput == null)
        {
            oldReader?.Dispose();
            return;
        }

        // Stop requests can complete asynchronously.
        // Dispose active playback resources after PlaybackStopped.
        if (oldOutput.PlaybackState == PlaybackState.Stopped)
        {
            oldOutput.Dispose();
            oldReader?.Dispose();
        }
        else
        {
            oldOutput.Stop();
        }
    }
}
