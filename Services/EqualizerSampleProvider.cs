using System;
using System.Linq;
using NAudio.Dsp;
using NAudio.Wave;

namespace MusicPlayer;

public sealed class EqualizerSampleProvider : ISampleProvider
{
    // Standard 10-band graphic equalizer frequencies.
    public static readonly float[] Frequencies =
    {
        31.25f, 62.5f, 125f, 250f, 500f,
        1000f, 2000f, 4000f, 8000f, 16000f
    };

    public const int BandCount = 10;

    private readonly ISampleProvider _source;
    private readonly BiQuadFilter[][] _filters;
    private readonly float[] _gains = new float[BandCount];
    private readonly bool[] _activeBands = new bool[BandCount];
    private readonly object _sync = new();

    private bool _enabled = true;
    private float _outputGain = 1f;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public EqualizerSampleProvider(ISampleProvider source)
    {
        _source = source;

        int sampleRate = WaveFormat.SampleRate;
        int channels = WaveFormat.Channels;

        _filters = new BiQuadFilter[BandCount][];

        for (int band = 0; band < BandCount; band++)
        {
            _filters[band] = new BiQuadFilter[channels];

            // A band must remain below the Nyquist frequency.
            _activeBands[band] =
                Frequencies[band] < sampleRate * 0.45f;

            float frequency = Math.Min(
                Frequencies[band],
                sampleRate * 0.45f);

            for (int channel = 0; channel < channels; channel++)
            {
                _filters[band][channel] = BiQuadFilter.PeakingEQ(
                    sampleRate,
                    frequency,
                    1.4f,
                    0f);
            }
        }
    }

    public void SetBandGain(int band, float gainDb)
    {
        if (band < 0 || band >= BandCount)
            throw new ArgumentOutOfRangeException(nameof(band));

        lock (_sync)
        {
            _gains[band] = Math.Clamp(gainDb, -12f, 12f);

            if (_activeBands[band])
            {
                for (int channel = 0;
                     channel < WaveFormat.Channels;
                     channel++)
                {
                    _filters[band][channel].SetPeakingEq(
                        WaveFormat.SampleRate,
                        Frequencies[band],
                        1.4f,
                        _gains[band]);
                }
            }

            UpdateHeadroom();
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
            _enabled = enabled;
    }

    private void UpdateHeadroom()
    {
        // Attenuate when bands are boosted to provide headroom.
        float maximumBoost = _gains.Max();

        float attenuationDb = -Math.Max(0f, maximumBoost);

        _outputGain = (float)Math.Pow(
            10.0,
            attenuationDb / 20.0);
    }

    public int Read(Span<float> buffer)
    {
        lock (_sync)
        {
            int samplesRead = _source.Read(buffer);

            if (!_enabled)
                return samplesRead;

            int channels = WaveFormat.Channels;

            for (int i = 0; i < samplesRead; i++)
            {
                int channel = i % channels;
                float sample = buffer[i];

                for (int band = 0; band < BandCount; band++)
                {
                    if (!_activeBands[band])
                        continue;

                    sample = _filters[band][channel]
                        .Transform(sample);
                }

                buffer[i] = sample * _outputGain;
            }

            return samplesRead;
        }
    }
}