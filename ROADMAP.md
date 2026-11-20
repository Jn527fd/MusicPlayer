# MusicPlayer Development Roadmap

A practical development plan for **MusicPlayer**, a Windows desktop music player built with **C# / WPF / .NET and NAudio**. The roadmap is ordered approximately from **easiest to hardest to implement**, based on the current application, rather than by importance alone.

> **Scope and estimates:** Time estimates are rough, focused implementation estimates—not deadlines. Integrations with Windows, audio devices, or large music collections may require additional debugging. Check off tasks only after they work in a release build.

## Current foundation

The application already includes local MP3/WAV playback, a searchable folder-based music library, per-song artwork, metadata display, waveforms and waveform seeking, frequency-band and loudness analysis, BPM estimation, playback controls, and a 10-band EQ with presets. Preserve these capabilities as the project grows.

## At a glance

| Order | Feature | Approx. effort | Main engineering skills |
| --- | --- | --- | --- |
| 1 | Persistent playback sessions and settings | 2–4 h | JSON, lifecycle, state management |
| 2 | User-defined EQ presets | 3–5 h | Persistence, validation, UI commands |
| 3 | Frequency-response graph for the equalizer | 4–7 h | DSP math, plotting, WPF rendering |
| 4 | Custom playlists and playlist management | 5–10 h | Collections, JSON, drag-and-drop |
| 5 | Windows media keys and system integration | 5–10 h | Win32/Windows APIs, lifecycle |
| 6 | Real-time post-EQ spectrum analyzer | 6–12 h | FFT, ring buffers, threading, DSP |
| 7 | SQLite-backed music library | 8–16 h | Relational schema, migrations, data access |
| 8 | Output gain, metering, and peak protection | 8–16 h | Gain staging, limiting, audio testing |
| 9 | Gapless playback and optional crossfade | 10–20 h | Stream mixing, state machines, buffering |
| 10 | Smart shuffle and rule-based smart playlists | 10–18 h | Ranking, audio features, UX |
| 11 | Audio-similarity recommendations with embeddings | 16–35+ h | Embeddings, retrieval, offline evaluation |

*The ordering is approximate: Windows integration may be more difficult than playlist work on some systems, and audio features can become significantly harder when aiming for glitch-free, production-quality behavior.*

---

## 1. Persistent playback sessions and settings

**Goal:** Launch MusicPlayer and resume where you left off.

- [ ] Define a versioned `AppSession` settings model with track path, playback position, volume, mute, shuffle/repeat, last folder, and active playlist.
- [ ] Persist the 10-band EQ gains and selected preset (or derive **Custom** when they do not match a preset).
- [ ] Save settings atomically in `%LOCALAPPDATA%\MusicPlayer\` to reduce the risk of a partially written file.
- [ ] Restore safely at startup; tolerate missing songs, deleted folders, or invalid settings.
- [ ] Restore the previous position **paused**, rather than automatically starting sound.
- [ ] Add a small schema version for future settings changes.

**Done when:** Closing and reopening the app restores the library, selected song, position, EQ, and playback preferences without crashing if the file moved.

**Suggested commit:** `feat: persist playback sessions and preferences`

## 2. User-defined equalizer presets

**Goal:** Allow users to save their own sound profiles.

- [ ] Add **Save Preset**, **Rename**, and **Delete** controls to the equalizer window.
- [ ] Store a preset name plus ten gain values in a JSON file.
- [ ] Load presets into the existing dropdown alongside built-in presets such as Flat, Bass Boost, Vocal, and V-Shape.
- [ ] Protect built-in presets from accidental deletion.
- [ ] Keep **Flat** at all zero gains; Reset always restores Flat.
- [ ] Validate preset names and gain ranges when loading data.

**Done when:** A custom curve survives a restart and can be selected or deleted without changing the built-in presets.

**Suggested commit:** `feat: add persistent custom equalizer presets`

## 3. Equalizer frequency-response graph

**Goal:** Visually explain what the EQ is doing to audio.

- [ ] Add a compact frequency-response graph above the ten sliders.
- [ ] Display frequency on a logarithmic x-axis and gain in dB on the y-axis.
- [ ] Calculate the **combined response** of all active biquad filters; do not simply draw straight lines between slider values.
- [ ] Update the curve when a band or preset changes.
- [ ] Display a flat 0 dB line when Reset is pressed.
- [ ] Test against expected single-band boosts and cuts.

**Done when:** The graph responds immediately and correctly reflects overlapping filters.

**Suggested commit:** `feat: visualize equalizer frequency response`

## 4. Custom playlists and playlist management

**Goal:** Let users organize music from multiple folders.

- [ ] Define playlist models with stable IDs, names, and ordered track references.
- [ ] Create, rename, delete, and select playlists from the main window.
- [ ] Add/remove tracks without deleting the underlying audio files.
- [ ] Support songs from multiple folders and reorder tracks with drag-and-drop or move-up/down buttons.
- [ ] Persist playlists in JSON initially; design the storage layer so it can move to SQLite later.
- [ ] Handle unavailable or moved files gracefully.
- [ ] Make Next, Previous, Shuffle, and search operate within the active playlist when applicable.

**Done when:** A playlist can be created, reordered, saved, reopened, and played end-to-end.

**Suggested commit:** `feat: add persistent playlist management`

## 5. Windows media keys and system integration

**Goal:** Control MusicPlayer without keeping its window focused.

- [ ] Support Play/Pause, Next, and Previous through Windows media controls.
- [ ] Publish current song title, artwork, duration, and playback status when supported.
- [ ] Keep Windows controls synchronized with seeks and track changes.
- [ ] Ensure handlers are registered once and released during shutdown.
- [ ] Test with keyboard media keys, Bluetooth headphones, and the Windows system media interface.
- [ ] Provide a graceful fallback for machines or environments where an integration is unavailable.

**Done when:** Hardware media buttons reliably control the running app and metadata stays in sync.

**Suggested commit:** `feat: integrate Windows media controls`

## 6. Real-time, post-EQ spectrum analyzer

**Goal:** Show the frequency content of the audio actually being played.

- [ ] Add a lightweight metering/tap stage **after the EQ** and before the audio output.
- [ ] Copy short sample windows to a bounded buffer without blocking the real-time audio callback.
- [ ] Compute FFTs off the audio thread and aggregate into 32 or 64 logarithmically spaced display bands.
- [ ] Render at a limited UI refresh rate, with smoothing and peak decay.
- [ ] Verify visual changes when EQ gains change (unlike the existing original-file analyzer).
- [ ] Avoid allocations and heavy locks in the output processing path where feasible.

**Done when:** The spectrum animates smoothly in sync with playback and responds to EQ changes without audible glitches.

**Suggested commit:** `feat: add real-time post-equalizer spectrum analyzer`

## 7. SQLite-backed music library

**Goal:** Scale beyond a single folder and make saved song information more reliable.

- [ ] Design tables for tracks, source folders, playlists, playlist tracks, audio features, artwork references, play history, and ratings.
- [ ] Use stable IDs and define rules for duplicate/moved files.
- [ ] Add asynchronous folder scans and incremental updates based on file metadata.
- [ ] Move existing JSON playlist data into the database through an explicit migration.
- [ ] Persist computed waveform/BPM/frequency metadata references and invalidation information.
- [ ] Add indexed searches and filtering for larger music libraries.
- [ ] Keep schema migrations repeatable and test with temporary databases.

**Done when:** Multiple folders and playlists remain consistent after restarts, rescans, and file changes.

**Suggested commit:** `feat: introduce SQLite-backed music library`

## 8. Output gain, improved metering, and peak protection

**Goal:** Make aggressive EQ settings less likely to distort the output.

- [ ] Add a user-adjustable preamp/output-gain control.
- [ ] Meter the post-EQ signal, including sample peak and clipping indicators.
- [ ] Introduce coefficient smoothing or crossfading to reduce clicks during slider changes.
- [ ] Evaluate and implement an appropriate limiter or safe gain stage, with documented latency and tradeoffs.
- [ ] Test boosted overlapping bands, low-volume passages, silent files, and rapid preset switching.
- [ ] Keep the audio callback free of expensive UI and disk work.

**Done when:** Normal and boosted presets remain stable, clipping is visible, and changing bands does not cause obvious clicks.

**Suggested commit:** `feat: improve equalizer gain staging and peak protection`

## 9. Gapless playback and optional crossfading

**Goal:** Transition between tracks without unintended gaps, with an optional fade between songs.

- [ ] Define a playback queue independent of the current ListBox selection.
- [ ] Preload the next track before the current one ends.
- [ ] Implement gapless transitions for compatible formats when possible.
- [ ] Add configurable crossfade duration and the ability to turn it off.
- [ ] Mix two decoded streams with a tested gain curve during crossfades.
- [ ] Define expected behavior for Repeat One, Shuffle, seeking near the end, and manual Next/Previous.
- [ ] Test different sample rates and channel layouts, and avoid leaking audio devices/readers.

**Done when:** Consecutive songs transition reliably, and crossfade can be enabled without breaking navigation.

**Suggested commit:** `feat: support gapless playback and crossfading`

## 10. Smart shuffle and rule-based smart playlists

**Goal:** Make song selection more intentional than pure randomness.

- [ ] Record play counts, skips, recent plays, and user favorites locally.
- [ ] Build shuffle rules that avoid immediate repeats and overly frequent tracks.
- [ ] Use BPM, duration, and simple spectral/energy features to form optional listening modes.
- [ ] Add smart-playlist rules such as **High Energy**, **Similar Tempo**, and **Not Played Recently**.
- [ ] Allow users to disable personalization and clear local listening history.
- [ ] Test recommendations with a varied library and provide understandable explanations.

**Done when:** Smart Shuffle produces reproducible, reasonable queues that differ meaningfully from random Shuffle.

**Suggested commit:** `feat: add smart shuffle and rule-based playlists`

## 11. Audio-embedding similarity and recommendations

**Goal:** Find songs that *sound alike*, even when filenames or metadata are unhelpful.

- [ ] Select and evaluate a suitable pretrained audio-embedding model with acceptable local inference requirements and licensing.
- [ ] Extract representative clips, compute embeddings in a background job, and cache results.
- [ ] Store embeddings and metadata in a queryable index or database.
- [ ] Implement **Find Similar Songs** using nearest-neighbor retrieval.
- [ ] Combine embedding similarity with tempo, energy, and user feedback when useful.
- [ ] Measure ranking quality on a small, manually labeled evaluation set.
- [ ] Display an explanation of the recommendation without overstating model confidence.
- [ ] Keep inference optional so the basic player still runs on lower-end systems.

**Done when:** A selected song returns relevant alternatives, results load quickly from cached embeddings, and quality is evaluated rather than assumed.

**Suggested commit:** `feat: add audio similarity search and recommendations`

---

## Recommended build sequence and dependencies

The order above follows approximate implementation difficulty. A few dependencies suggest adjusting the *actual* build order slightly:

- Build **persistent settings** first; custom EQ presets can reuse the persistence patterns.
- Implement **playlist management** before the SQLite migration, but avoid tying playlists to a single folder.
- Build **post-EQ metering/spectrum** before advanced peak protection so you can see clipping and filter effects.
- Introduce a proper **playback queue** before implementing crossfades or smart shuffle.
- Implement **SQLite and cached audio features** before embedding-based recommendations.

## Engineering standards for every milestone

- [ ] Preserve existing playback, waveform seeking, EQ, BPM, and playlist behavior.
- [ ] Keep UI updates on WPF's dispatcher; perform decoding, file scanning, and ML inference off the UI thread.
- [ ] Support cancellation and cleanup for long-running jobs and playback resources.
- [ ] Handle missing, unreadable, or corrupt audio files gracefully.
- [ ] Add targeted tests for non-UI logic and a short manual regression checklist.
- [ ] Update the project README and screenshots when the user-visible behavior changes.
- [ ] Use one focused Git commit per completed milestone.

## Long-term vision

**MusicPlayer** can evolve in three complementary directions:

1. **A complete desktop music player:** Persistent sessions, custom playlists, library database, Windows integration, and reliable playback.
2. **An audio engineering tool:** Real-time analysis, accurate EQ response visualization, gain staging, and seamless transitions.
3. **An intelligent music application:** Smart playlists, audio embeddings, similarity search, and locally evaluated recommendations.

The aim is a dependable music player first, with increasingly advanced audio and ML capabilities layered on top.
