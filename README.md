# MusicPlayer

MusicPlayer is a Windows desktop app built with C# and WPF for playing local MP3 and WAV files. It combines a searchable music library with playback-synced audio analysis, automatic BPM estimation, and a 10-band graphic equalizer.

## Getting started

Install the .NET 10 SDK on Windows, then run these commands in PowerShell:

```powershell
git clone https://github.com/Jn527fd/MusicPlayer.git
cd MusicPlayer
dotnet run --project MusicPlayer.csproj
```

From the **File** menu, choose **Open Folder** to list its MP3 and WAV files. The app scans the selected folder only. **Open File** loads the containing folder and selects the chosen track. Use the search box to filter by filename, then double-click a track to play it. Use **Set Default Folder** to reopen a folder at startup.

## Features

- Play and pause, skip to the previous or next track, shuffle, repeat the current track, seek, adjust volume, and mute.
- View duration, format, sample rate, channel count, and bitrate when available. The song list and Now Playing panel show embedded BPM when available and estimate it from the audio when a track without a BPM tag is opened.
- See a full-track waveform with a moving playhead. The app generates 400 peak values in the background and caches them for future opens.
- Inspect relative bass, midrange, and treble energy and see RMS and peak levels in dBFS at the current playback position. These measurements describe the source audio. Levels below digital full scale appear as negative values.
- Attach custom artwork from a local image or URL. The default folder setting, custom artwork, waveform cache, and tempo cache are stored under `%LOCALAPPDATA%\MusicPlayer`.

## Automatic BPM estimation

When you open a track without an embedded BPM tag, MusicPlayer analyzes it in the background. It uses spectral flux to detect onsets and normalized autocorrelation to find repeating beat intervals between 60 and 200 BPM. Now Playing displays the estimated BPM and a rhythm strength score. If the signal is too short or the rhythm is too weak to estimate, the BPM field shows `-`.

Switching tracks cancels the previous analysis. Successful estimates are cached using the file path, size, modification time, and detector version.

## Graphic equalizer

Open **Sound > Equalizer** to adjust 10 frequency bands from 31.25 Hz to 16 kHz. Each slider cuts or boosts its band by up to 12 dB during playback. The equalizer starts at **Flat**, with every band at 0 dB. Choose **Bass Boost**, **Vocal**, or **V-Shape** from the preset menu, or move a slider to create a **Custom** setting. **Reset** restores **Flat**.

The equalizer is always enabled. Settings carry over when you switch tracks during the current session.

## Project structure

| File | Role |
| --- | --- |
| [`MainWindow.xaml`](MainWindow.xaml) and [`MainWindow.xaml.cs`](MainWindow.xaml.cs) | WPF layout, library browsing, playback controls, and visualization updates. |
| [`SongFile.cs`](SongFile.cs) | Per-track metadata, tempo estimates, and artwork used by the song list and Now Playing panel. |
| [`EqualizerWindow.xaml`](EqualizerWindow.xaml) and [`EqualizerWindow.xaml.cs`](EqualizerWindow.xaml.cs) | Equalizer layout, band sliders, and preset controls. |
| [`Services/AudioPlaybackService.cs`](Services/AudioPlaybackService.cs) | NAudio decoding and `WaveOut` playback, with the equalizer in the playback pipeline. |
| [`Services/EqualizerSampleProvider.cs`](Services/EqualizerSampleProvider.cs) | Per-channel biquad filters and output attenuation to provide headroom when bands are boosted. |
| [`Services/AudioAnalysisService.cs`](Services/AudioAnalysisService.cs) | TagLibSharp metadata reading and NAudio waveform, frequency, level, and tempo analysis. |

## Planned work

- Save and manage custom equalizer presets.
