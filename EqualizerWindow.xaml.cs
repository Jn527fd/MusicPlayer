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

        // Store the existing audio playback service.
        _player = player;

        // Create the 10 frequency sliders.
        BuildBandControls();

        // Determine whether the current EQ is flat.
        bool isFlat = true;

        for (int i = 0; i < _sliders.Length; i++)
        {
            // Restore the gain for each frequency band.
            _sliders[i].Value = player.GetBandGain(i);

            if (Math.Abs(_sliders[i].Value) > 0.001)
                isFlat = false;
        }

        // The equalizer is always enabled.
        _player.SetEqualizerEnabled(true);

        // Show Flat if all gains are zero; otherwise show Custom.
        PresetBox.SelectedIndex = isFlat ? 1 : 0;

        // Allow slider events after initialization is complete.
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

            // Main container for each frequency band.
            Grid column = new();

            column.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            column.RowDefinitions.Add(
                new RowDefinition {
                    Height = new GridLength(1, GridUnitType.Star)
                });

            column.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            column.Margin = new Thickness(2, 0, 2, 0);

            // Current gain displayed above the slider.
            TextBlock gainLabel = new()
            {
                Text = "0",
                FontSize = 11,
                Foreground = Brushes.LightSkyBlue,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };

            Grid.SetRow(gainLabel, 0);

            // Dark card containing the vertical slider.
            Border sliderCard = new()
            {
                Background = new SolidColorBrush(
                    Color.FromRgb(43, 60, 81)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 12, 6, 12),
                Margin = new Thickness(0, 0, 0, 7)
            };

            Slider slider = new()
            {
                Orientation = Orientation.Vertical,
                Minimum = -12,
                Maximum = 12,
                Value = 0,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = (Style)FindResource("EqSliderStyle")
            };

            sliderCard.Child = slider;
            Grid.SetRow(sliderCard, 1);

            // Frequency label below the card.
            float frequency =
                EqualizerSampleProvider.Frequencies[band];

            string frequencyLabel = frequency >= 1000
                ? $"{frequency / 1000:0.#} kHz"
                : $"{frequency:0.#}";

            TextBlock nameLabel = new()
            {
                Text = frequencyLabel,
                FontSize = 10,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(181, 200, 220)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };

            Grid.SetRow(nameLabel, 2);

            // Update the audio filter whenever a slider moves.
            slider.ValueChanged += (_, _) =>
            {
                if (!_ready || _applyingPreset)
                    return;

                float gain = (float)slider.Value;

                _player.SetBandGain(index, gain);

                gainLabel.Text =
                    $"{gain:+0;-0;0}";

                // Manual adjustment means Custom preset.
                PresetBox.SelectedIndex = 0;
            };

            column.Children.Add(gainLabel);
            column.Children.Add(sliderCard);
            column.Children.Add(nameLabel);

            BandsPanel.Children.Add(column);

            _sliders[band] = slider;
            _valueLabels[band] = gainLabel;
        }
    }

    private void UpdateValueLabels()
    {
        for (int i = 0; i < _sliders.Length; i++)
        {
            float value = (float)_sliders[i].Value;

            _valueLabels[i].Text =
                $"{value:+0;-0;0}";
        }
    }

    private void PresetChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_ready || _applyingPreset)
            return;

        int index = PresetBox.SelectedIndex;

        if (index <= 0)
            return;

        ApplyPreset(Presets[index - 1]);
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
        object sender,
        RoutedEventArgs e)
    {
        ApplyPreset(Presets[0]);
        PresetBox.SelectedIndex = 1;
    }
}
