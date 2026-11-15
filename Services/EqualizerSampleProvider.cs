
using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace MusicPlayer;

public sealed class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly BiQuadFilter[][] _filters;
    private readonly object _sync = new();

    private readonly float[] _frequencies = { 100f, 1000f, 10000f };
    private readonly float[] _gains = { 0f, 0f, 0f };

    private bool _enabled = true;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public EqualizerSampleProvider(ISampleProvider source)
    {
        _source = source;

        int channels = source.WaveFormat.Channels;
        int sampleRate = source.WaveFormat.SampleRate;

        _filters = new BiQuadFilter[3][];

        for (int band = 0; band < 3; band++)
        {
            _filters[band] = new BiQuadFilter[channels];

            for (int channel = 0; channel < channels; channel++)
            {
                float frequency = Math.Min(
                    _frequencies[band],
                    sampleRate * 0.45f);

                _filters[band][channel] =
                    BiQuadFilter.PeakingEQ(
                        sampleRate,
                        frequency,
                        0.8f,
                        0f);
            }
        }
    }

    public void SetGains(float bass, float mid, float treble)
    {
        lock (_sync)
        {
            _gains[0] = Math.Clamp(bass, -9f, 9f);
            _gains[1] = Math.Clamp(mid, -9f, 9f);
            _gains[2] = Math.Clamp(treble, -9f, 9f);

            for (int band = 0; band < 3; band++)
            {
                float frequency = Math.Min(
                    _frequencies[band],
                    WaveFormat.SampleRate * 0.45f);

                foreach (BiQuadFilter filter in _filters[band])
                {
                    filter.SetPeakingEq(
                        WaveFormat.SampleRate,
                        frequency,
                        0.8f,
                        _gains[band]);
                }
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
            _enabled = enabled;
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

                for (int band = 0; band < 3; band++)
                {
                    sample = _filters[band][channel]
                        .Transform(sample);
                }

                buffer[i] = sample;
            }

            return samplesRead;
        }
    }
}
