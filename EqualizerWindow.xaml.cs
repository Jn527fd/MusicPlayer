using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MusicPlayer;

public partial class EqualizerWindow : Window
{
    private readonly AudioPlaybackService _player;

    private readonly Slider[] _sliders =
        new Slider[EqualizerSampleProvider.BandCount];

    private readonly TextBlock[] _valueLabels =
        new TextBlock[EqualizerSampleProvider.BandCount];

    private bool _ready;
    private bool _applyingPreset;

    private static readonly float[][] Presets =
    {
        new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        new float[] { 5, 5, 4, 2, 0, 0, 0, 0, 0, 0 },
        new float[] { -2, -1, 0, 1, 2, 3, 3, 1, 0, -1 },
        new float[] { 4, 3, 1, -1, -2, -1, 1, 3, 4, 3 }
    };

    public EqualizerWindow(AudioPlaybackService player)
    {
        InitializeComponent();

        _player = player;
        BuildBandControls();

        for (int i = 0; i < _sliders.Length; i++)
            _sliders[i].Value = player.GetBandGain(i);

        EnableEqCheck.IsChecked = player.EqualizerEnabled;
        PresetBox.SelectedIndex = 0;

        _ready = true;
        UpdateValueLabels();
    }

    private void BuildBandControls()
    {
        for (int band = 0;
             band < EqualizerSampleProvider.BandCount;
             band++)
        {
            int index = band;

            Grid row = new()
            {
                Margin = new Thickness(0, 5, 0, 5)
            };

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(80)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(65)
                });

            float frequency =
                EqualizerSampleProvider.Frequencies[band];

            string label = frequency >= 1000
                ? $"{frequency / 1000:0.#} kHz"
                : $"{frequency:0.#} Hz";

            TextBlock name = new()
            {
                Text = label,
                Foreground = Brushes.LightGray,
                VerticalAlignment = VerticalAlignment.Center
            };

            Slider slider = new()
            {
                Minimum = -12,
                Maximum = 12,
                Value = 0,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(8, 0, 8, 0)
            };

            TextBlock value = new()
            {
                Text = "0 dB",
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            slider.ValueChanged += (_, _) =>
            {
                if (!_ready || _applyingPreset)
                    return;

                _player.SetBandGain(
                    index, (float)slider.Value);

                value.Text = $"{slider.Value:+0;-0;0} dB";

                // A manual adjustment creates a custom EQ curve.
                PresetBox.SelectedIndex = 0;
            };

            Grid.SetColumn(name, 0);
            Grid.SetColumn(slider, 1);
            Grid.SetColumn(value, 2);

            row.Children.Add(name);
            row.Children.Add(slider);
            row.Children.Add(value);

            BandsPanel.Children.Add(row);

            _sliders[band] = slider;
            _valueLabels[band] = value;
        }
    }

    private void UpdateValueLabels()
    {
        for (int i = 0; i < _sliders.Length; i++)
            _valueLabels[i].Text =
                $"{_sliders[i].Value:+0;-0;0} dB";
    }

    private void EqEnabledChanged(
        object sender, RoutedEventArgs e)
    {
        if (_ready)
            _player.SetEqualizerEnabled(
                EnableEqCheck.IsChecked == true);
    }

    private void PresetChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _applyingPreset)
            return;

        int presetIndex = PresetBox.SelectedIndex;

        // Index 0 is Custom.
        if (presetIndex <= 0)
            return;

        ApplyPreset(Presets[presetIndex - 1]);
    }

    private void ApplyPreset(float[] gains)
    {
        _applyingPreset = true;

        try
        {
            for (int i = 0; i < _sliders.Length; i++)
            {
                _sliders[i].Value = gains[i];
                _player.SetBandGain(i, gains[i]);
            }

            UpdateValueLabels();
        }
        finally
        {
            _applyingPreset = false;
        }
    }

    private void ResetEq_Click(
        object sender, RoutedEventArgs e)
    {
        ApplyPreset(Presets[0]);
        PresetBox.SelectedIndex = 1; // Flat
    }
}
