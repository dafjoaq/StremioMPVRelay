# 2026-08-16
Project initialized in JetBrains Rider as a .NET 8 WinForms app.

### Workflow
Stremio addon -> stream selection -> MPV -> rolling queue -> history/progress JSON

### Priority files
1. `Models/AppSettings.cs` - application settings model
2. `Models/StreamInfo.cs` - Stremio stream model
3. `Infrastructure/AtomicJsonFile.cs` + `Services/SettingsService.cs` - safe settings load/save
4. `Services/StremioAddonService.cs` - fetch and parse addon streams
5. `Services/StreamSelector.cs` - filter and rank streams
6. `Services/MpvService.cs` - MPV IPC and playback control
7. `Services/RollingQueueService.cs` - queue, retry, buffering, recovery
8. `MainForm.cs` + `Program.cs` - UI wiring and application startup
9. `Services/LibraryService.cs`
   - read `StremioMpvLibrary.json`
   - find series by IMDb ID + season
   - save current episode
   - save position/duration/completed state
   - retrieve resume progress
10. `MainForm.cs` + `MainForm.Designer.cs`
    - add History/Library selector
    - load entries from `LibraryService`
    - selecting a series fills:
      - IMDb ID
      - Title
      - Season
      - First episode
      - Last episode
    - refresh history after playback changes
11. `RollingQueueService.cs` + `LibraryService.cs`
    - update history when episode starts
    - periodically save playback position
    - mark episode completed on successful `end-file`
    - advance `CurrentEpisode`
    - preserve incomplete episode resume position
12. `CinemetaService.cs`
    - retrieve series metadata when needed
    - resolve/display proper series title
    - reduce manual metadata entry
13. `RollingQueueService.cs`
    - verify failed stream detection
    - exclude failed URL
    - fetch another stream
    - replace failed playlist entry
    - continue without skipping episode
14. Final cleanup/testing
    - settings persistence
    - history persistence
    - resume after restart
    - queue transitions
    - failure recovery
    - Lua controls
    - release build

### After core playback
- `Services/LibraryService.cs` - load/save watch history and progress
- `MainForm.Designer.cs` - UI layout
- `Services/CinemetaService.cs` - metadata lookup

### Main data files
- `StremioMPVRelay.settings.json`
- `StremioMpvLibrary.json`
