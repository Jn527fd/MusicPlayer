using System;
using System.Windows;
using System.Windows.Controls;

namespace MusicPlayer;

public partial class EqualizerWindow : Window
{
    private readonly AudioPlaybackService _player;
    private bool _ready;

    public EqualizerWindow(AudioPlaybackService player)
    {
        InitializeComponent();

        _player = player;

        // Restore the current EQ settings.
        BassSlider.Value = player.BassGain;
        MidSlider.Value = player.MidGain;
        TrebleSlider.Value = player.TrebleGain;

        EnableEqCheck.IsChecked = player.EqualizerEnabled;

        _ready = true;
        UpdateEqualizer();
    }

    private void EqSliderChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ready)
            UpdateEqualizer();
    }

    private void EqEnabledChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (_ready)
            UpdateEqualizer();
    }

    private void UpdateEqualizer()
    {
        float bass = (float)BassSlider.Value;
        float mid = (float)MidSlider.Value;
        float treble = (float)TrebleSlider.Value;

        // Apply the new gains to the live audio pipeline.
        _player.SetEqualizer(bass, mid, treble);

        _player.SetEqualizerEnabled(
            EnableEqCheck.IsChecked == true);

        // Show the current gain values.
        BassValue.Text = $"{bass:+0;-0;0} dB";
        MidValue.Text = $"{mid:+0;-0;0} dB";
        TrebleValue.Text = $"{treble:+0;-0;0} dB";
    }

    private void ResetEq_Click(object sender, RoutedEventArgs e)
    {
        BassSlider.Value = 0;
        MidSlider.Value = 0;
        TrebleSlider.Value = 0;

        UpdateEqualizer();
    }
}
