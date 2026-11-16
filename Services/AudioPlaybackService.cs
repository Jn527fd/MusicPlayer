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

    private readonly float[] _eqGains =
        new float[EqualizerSampleProvider.BandCount];

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

    private void ApplyVolume()
    {
        if (_reader != null)
            _reader.Volume = _isMuted ? 0f : (float)_volume;
    }

   
    public bool EqualizerEnabled => _eqEnabled;

    public float GetBandGain(int band)
    {
        return _eqGains[band];
    }

    public void SetBandGain(int band, float gainDb)
    {
        if (band < 0 || band >= _eqGains.Length)
            throw new ArgumentOutOfRangeException(nameof(band));

        float gain = Math.Clamp(gainDb, -12f, 12f);

        _eqGains[band] = gain;
        _equalizer?.SetBandGain(band, gain);
    }

    public void SetEqualizerEnabled(bool enabled)
    {
        _eqEnabled = enabled;
        _equalizer?.SetEnabled(enabled);
    }

    public void ResetEqualizer()
    {
        for (int i = 0; i < _eqGains.Length; i++)
            SetBandGain(i, 0f);
    }

    public void Open(Uri uri)
    {
        Close();

        try
        {
            AudioFileReader reader = new(uri.LocalPath);
            EqualizerSampleProvider equalizer = new(reader);

            for (int i = 0; i < _eqGains.Length; i++)
                equalizer.SetBandGain(i, _eqGains[i]);

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
