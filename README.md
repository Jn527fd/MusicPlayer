# MusicPlayer

MusicPlayer is a Windows desktop app for playing local MP3 and WAV files and inspecting their audio. It combines a searchable folder view with a waveform and signal measurements that follow the playback position.

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
- View duration, format, sample rate, channel count, and bitrate when available. The BPM field currently displays the embedded tempo tag or `-` when the tag is unavailable.
- See a full-track waveform with a moving playhead. The app generates 400 peak values in the background and caches them for future opens.
- Inspect relative bass, midrange, and treble energy and see RMS and peak levels in dBFS at the current playback position. Levels below digital full scale appear as negative values.
- Attach custom artwork from a local image or URL. The default folder setting, custom artwork, and waveform cache are stored under `%LOCALAPPDATA%\MusicPlayer`.

## Project structure

| File | Role |
| --- | --- |
| [`MainWindow.xaml`](MainWindow.xaml) and [`MainWindow.xaml.cs`](MainWindow.xaml.cs) | WPF layout, library browsing, playback controls, and visualization updates. |
| [`SongFile.cs`](SongFile.cs) | Per-track metadata and artwork used by the song list and Now Playing panel. |
| [`Services/AudioPlaybackService.cs`](Services/AudioPlaybackService.cs) | Playback through WPF `MediaPlayer`. |
| [`Services/AudioAnalysisService.cs`](Services/AudioAnalysisService.cs) | Metadata reading with TagLibSharp, plus waveform and FFT-based signal analysis with NAudio. |

## Next step: automatic BPM detection

Estimate a song's tempo directly from its audio, including tracks with missing BPM tags. A first implementation can analyze each track when it is opened and cache the result for later visits.

1. Add a cancellable tempo analysis method to `AudioAnalysisService`. Decode audio in the background, calculate a beat-onset envelope using spectral flux, and use autocorrelation to find likely beat intervals within a configurable tempo range.
2. Keep embedded BPM and detected BPM as separate values. Display the estimate in the song list and Now Playing panel once available, label it as detected, and fall back to the embedded tag if the estimate is inconclusive. Leave the field blank with `-` if neither value is available.
3. Cache the estimate by file path, file size, modification time, and detector version so changed tracks and algorithm updates trigger fresh analysis.
4. Check accuracy with known-tempo click tracks and music with verified BPM. Include half-time and double-time ambiguity, silence, and tracks with weak or changing rhythms. Verify that changing songs during analysis keeps playback responsive and prevents stale results from updating the wrong track.
