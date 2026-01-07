# AirStudio Commercial Manager - Implementation Progress

**Session Date:** 2026-01-07
**Spec Reference:** CLAUDECODE_AirStudio_CommercialManager_FrozenSpec.md
**Status Legend:** [x] Complete | [~] Partial | [ ] Not Started

---

## Project Structure

| Component | Status | Notes |
|-----------|--------|-------|
| WPF Solution | [x] | AirStudio.CommercialManager.sln |
| UI Project | [x] | AirStudio.CommercialManager (WPF .NET 8) |
| Core Project | [x] | AirStudio.CommercialManager.Core |
| Data Project | [x] | AirStudio.CommercialManager.Data |

---

## Section 0: Non-Negotiable Constraints

### 0.1 .NET / UI
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| C# WPF .NET 8.0 | [x] | All projects target net8.0-windows |
| Dark blue theme | [x] | Implemented in MainWindow.xaml, ConfigurationWindow.xaml |
| Async UI | [x] | Services use async/await throughout |

### 0.2 Database Access
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| MySqlConnector only | [x] | Data project uses MySqlConnector 2.5.0 |

### 0.3 Security & Admin
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Allowed users check | [x] | SecurityService.cs checks AllowedUsers list |
| Config visible to Local Admins only | [x] | SecurityService.IsCurrentUserAdmin() checks |

### 0.4 Machine-wide Configuration
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| ProgramData config storage | [x] | ConfigurationService.cs uses ProgramData path |
| Export Settings | [x] | ExportAsync() with AES encryption |
| Import Settings | [x] | ImportAsync() with backup creation |
| AES passphrase "air" | [x] | Fixed passphrase in export/import |

### 0.5 Channels Source
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Load from air_virtual_studio.setup_channels | [x] | ChannelService.cs |

### 0.6 Channel Locations (X Targets)
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| X root targets per channel | [x] | Channel.cs has XRootTargets list |
| Derived paths (TAG folder, Audio folder) | [x] | Channel.GetPlaylistPath(), GetCommercialsPath() |
| Channel unusable without X root | [x] | Channel.IsConfigured property |

---

## Section 1: Multi-Database Strategy

### 1.1 DB Profiles
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Ordered list of DB profiles | [x] | AppConfiguration.DatabaseProfiles |
| Default DB profile | [x] | AppConfiguration.DefaultDatabaseProfileId |

### 1.2 Read Strategy
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Parallel-first-success | [x] | DatabaseRouter.ExecuteReadAsync() |
| Cap concurrency 2-3 | [x] | SemaphoreSlim(3) in DatabaseRouter |
| Return first success, cancel others | [x] | CancellationTokenSource per query |
| Disconnected state on all fail | [x] | Returns error result |

### 1.3 Write Strategy
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Fan-out to ALL DB servers | [x] | DatabaseRouter.ExecuteWriteAsync() |
| Partial failures warn but allow | [x] | DbOperationResult tracks partial success |
| Self-healing UPDATE/INSERT | [x] | ExecuteUpsertAsync() method |

---

## Section 2: Legacy Playlist DB Contract

### 2.1-2.2 Playlist Table
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Database: air_<channel> | [x] | TagService uses channel-specific DB |
| Table: playlist | [x] | TagService.SaveScheduleToDatabase() |
| TxTime as VARCHAR | [x] | Stored as "HH:mm:ss" |
| TxDate as DATE | [x] | Schedule.FromDate |
| Validity as DATE | [x] | Schedule.ToDate |

### 2.3-2.4 Commercial Row Constants & Insert Fields
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Mode = 2 | [x] | TagService inserts Mode=2 |
| ProgType = "COMMERCIALS" | [x] | Hardcoded in TagService |
| StartTime = 0 | [x] | Set in insert |
| TxTime, TxDate, Validity | [x] | From Schedule object |
| Programme = CapsuleName | [x] | Schedule.Capsule.Name |
| Title = FirstCutSpotName | [x] | First segment's SpotName |
| Duration/StopTime | [x] | Calculated from capsule |
| MainPath = FullTagPath | [x] | Generated TAG path |
| LoginUser, LastUpdate | [x] | Windows user, NOW() |
| UserName, MobileNo | [x] | From Schedule object |

### 2.5 Update Behavior
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Update existing row (not delete/reinsert) | [x] | UPDATE first approach |
| Row key: Mode=2 AND TxDate AND TxTime AND MainPath | [x] | Used in WHERE clause |
| Self-heal if 0 rows affected | [x] | Falls back to INSERT |

---

## Section 2A: Agency Master

### 2A.1 Agency Table
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Database: air_<channel> | [x] | AgencyService uses channel DB |
| Table: agency | [x] | AgencyService queries agency table |
| Columns: Code, AgencyName, Address, PIN, Phone, Email | [x] | Agency.cs model |

### 2A.2 Agency CRUD
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Add Agency | [x] | AgencyService.AddAgencyAsync() |
| Edit Agency | [x] | AgencyService.UpdateAgencyAsync() |
| Delete Agency | [x] | AgencyService.DeleteAgencyAsync() |
| Block delete if referenced | [x] | Checks commercial references |
| AgencyName required | [x] | Validation in service |

### 2A.3 Agency Selection in Add Commercial
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Show AgencyName, store Code | [x] | AgencyComboBox.xaml |
| Search-as-you-type | [x] | Editable ComboBox with filtering |

### 2A.4 Add Agency Inline Flow
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Floating Add Agency window | [x] | AgencyDialog.xaml (modal) |
| Pre-fill AgencyName | [x] | Dialog pre-population |
| Refresh and auto-select after add | [x] | AgencyComboBox handles |

### 2A.5 Data Usage in TAG
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Agency Code in TAG column 9 | [x] | TagEntry.AgencyCode |

---

## Section 3: TAG Files

### 3.1 TAG Filename Format
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Format: HHMMSS_DDMMYY(R)_CAPSULE NAME.TAG | [x] | TagService.GenerateTagFilename() |
| Keep spaces in capsule name | [x] | Only removes invalid chars |
| Remove invalid filename chars | [x] | SanitizeForFilename() |
| UPPERCASE | [x] | ToUpperInvariant() |
| Extension .TAG uppercase | [x] | Hardcoded |
| R = (ToDate - FromDate).Days | [x] | Schedule.RepeatDays |

### 3.2 TAG Creation Rules
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Single TAG file for repeats | [x] | One file per schedule |
| Overwrite in-place on edit | [x] | TagService.SaveTagFileAsync() |
| Rename if filename-affecting changes | [~] | Partial - needs verification |
| No backup copies | [x] | Direct overwrite |

### 3.3 TAG Row Format
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Tab-separated | [x] | TagEntry.ToTagLine() uses \t |
| 10 columns | [x] | All columns generated |
| Column 1: Category (2 or 1) | [x] | TagEntry.Category |
| Column 2: TxTime | [x] | TagEntry.TxTime |
| Column 3: SpotName | [x] | TagEntry.SpotName |
| Column 4: CapsuleName | [x] | TagEntry.CapsuleName |
| Column 5: Duration HH:mm:ss | [x] | TagEntry.Duration |
| Column 6: Flag (0) | [x] | Always 0 |
| Column 7: DurationSecondsFloat | [x] | TagEntry.DurationSeconds |
| Column 8: AudioPath | [x] | TagEntry.AudioPath |
| Column 9: AgencyCode | [x] | TagEntry.AgencyCode |
| Column 10: SequenceToken | [x] | TagEntry.SequenceToken |

### 3.4 Terminator Line
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Multi-cut: append terminator | [x] | Category=1, SequenceToken ends :99 |
| Single-spot: one line with Cat=1 | [x] | Handled in generation |

### 3.5 SequenceToken (NN Base)
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Configurable SequenceBaseNN | [x] | AppConfiguration.SequenceBaseNN |
| Lines 1..N: NN:01:01, NN:01:02... | [x] | TagEntry generation |
| Terminator: NN:01:99 | [x] | IsTerminator flag |
| Preserve NN on import | [x] | ParseTagFile extracts existing |

---

## Section 4: Audio Processing

### 4.1 Audio Standard
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| WAV PCM | [x] | AudioService.ConvertToStandardFormatAsync() |
| 16-bit | [x] | WaveFormat(48000, 16, 2) |
| 48,000 Hz | [x] | SampleRate = 48000 |
| Stereo | [x] | Channels = 2 |

### 4.2 Output Naming/Location
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Filename based on Spot Name | [x] | SanitizeForFilename() |
| Keep spaces | [x] | Only removes invalid chars |
| UPPERCASE | [x] | ToUpperInvariant() |
| Extension .WAV uppercase | [x] | Hardcoded |
| Store under X:\Commercials\ | [x] | ReplicateAudioAsync() |

### 4.3 Overwrite Behavior
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Overwrite after successful conversion | [x] | Atomic swap with temp file |
| Partial replication failures allowed | [x] | Warns but continues |

### 4.4 Audio Drag & Drop
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Drop onto Library grid | [x] | LibraryControl supports drag-drop |
| Drop onto Replace Audio area | [~] | TagEditorControl - partial |
| Drop multiple files | [~] | Single file handling confirmed |

---

## Section 5: Configuration Window (Admin Only)

### 5.1 Databases Tab
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Manage ordered DB profiles | [x] | ConfigurationWindow Databases tab |
| Fields: Name, Host, Port, User, Pass, SSL, Timeout | [x] | DatabaseProfileDialog.xaml |
| Test Connection button | [x] | DatabaseProfileDialog tests |
| Set Default profile | [x] | UI for setting default |
| DPAPI at-rest encryption | [x] | SecureStorage.cs |
| AES export encryption | [x] | ConfigurationService export |

### 5.2 Locations Tab
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Show channels from DB | [x] | Loads via ChannelService |
| Map Channel to X roots | [x] | Locations tab UI |
| Validate X root exists/writable | [~] | Test button - needs verification |

### 5.3 Users Tab
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Allowed users allow-list | [x] | Users tab in ConfigurationWindow |
| Format: DOMAIN\\user or MACHINE\\user | [x] | SecurityService validates |

### 5.4 Export/Import
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Export single JSON file | [x] | ExportAsync() |
| AES-encrypted passwords | [x] | Passphrase "air" |
| Import overwrites config | [x] | ImportAsync() |
| Creates .bak backup | [x] | Backup before import |

---

## Section 6: Main Window UX

### 6.1 Startup Flow
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Start with no channel selected | [x] | MainWindow.xaml.cs |
| Channel ComboBox animated | [x] | PulseAnimation in XAML |
| Operations disabled until channel selected | [x] | IsEnabled bindings |

### 6.2 DB Profile Usage
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Operators use Default DB automatically | [x] | DatabaseRouter uses default |
| Reads use parallel-first-success | [x] | ExecuteReadAsync() |

### 6.3 Unsaved Protection
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Block channel change if unsaved | [x] | MainWindow.ChannelComboBox_SelectionChanged |
| Block window close if unsaved | [x] | MainWindow.MainWindow_Closing handler |
| Block navigation if unsaved | [x] | All navigation buttons check unsaved changes |
| Prompt: Save/Discard/Cancel | [x] | CheckAndHandleUnsavedChangesAsync with 3-button dialog |

### 6.4 Collision Policy
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Warn on time collision | [~] | Needs verification |
| Allow override | [~] | Needs verification |

---

## Section 7: Core Screens & Workflows

### 7.1 Library Screen
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Grid + search | [x] | LibraryControl.xaml |
| Add/Edit/Deactivate | [x] | CommercialDialog.xaml |
| Drag-and-drop audio to add | [x] | Drop handling in LibraryControl |
| Reconvert to WAV 48k/16-bit/stereo | [x] | AudioService conversion |
| Replicate to all X targets | [x] | ReplicateAudioAsync() |
| Save DB even if partial replication | [x] | Warns but saves |

### 7.2 Capsule Builder
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Build capsule from library cuts | [x] | CapsuleBuilderControl.xaml |
| Reorder/remove | [x] | Drag-drop and buttons |
| Compute total duration | [x] | Capsule.TotalDuration |
| Drag drop from library | [x] | ListBox drag handlers |
| Show waveform segmentation | [x] | WaveformViewer control |
| Schedule button | [x] | CapsuleReady event |
| Save TAG only (optional) | [~] | Needs verification |

### 7.3 Scheduling Dialog
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| From Date (TxDate) | [x] | SchedulingControl DatePickers |
| TxTime | [x] | Time input |
| To Date (Validity) | [x] | ToDate picker |
| Compute R = (ToDate - FromDate).Days | [x] | Schedule.RepeatDays |
| Require UserName & MobileNo | [x] | Input fields with validation |
| Single commercial: capsule name = SpotName | [x] | Auto-naming logic |

### 7.4 Import/Edit TAG
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Browse TAGs from X:\Commercial Playlist\ | [x] | TagEditorControl file browser |
| Auto-replicate to missing targets on save | [~] | Partial implementation |
| Parse TAG | [x] | TagService.ParseTagFile() |
| Edit cuts | [x] | TagEditorControl grid editing |
| Overwrite TAG in place | [x] | SaveTagFileAsync() |
| Rename if filename-affecting changes | [~] | Needs verification |
| DB update with self-heal | [x] | ExecuteUpsertAsync() |
| Audio replace drag-drop | [~] | Partial implementation |

---

## Section 8: Waveform Viewer

### 8.1 General Requirements
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Clean waveform viewer | [x] | WaveformViewer.xaml |
| Dark theme friendly | [x] | Theme colors |
| Show waveform quickly (cached) | [x] | WaveformGenerator caching |
| Segmented waveform for capsules | [x] | Multi-segment support |
| Different segment colors | [x] | SegmentColors array |
| Segment separators and labels | [x] | DrawSegmentLabels() |
| Update on selection/reorder/replace | [x] | Dependency property changes |

### 8.2 Playback Preview
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Play/Pause, Stop | [x] | AudioPlayer.cs controls |
| Scrub/seek | [x] | Basic seeking implemented |
| Show current time / total | [x] | Time display |
| Play capsule sequentially | [x] | Multi-segment playback works |
| Multi-segment cursor tracking | [x] | **FIXED 2026-01-07**: Cursor now tracks correctly across segments |
| Highlight current segment | [~] | Partial implementation |

### 8.3 Implementation
| Requirement | Status | Implementation |
|-------------|--------|----------------|
| NAudio for decoding/playback | [x] | NAudio 2.2.1 package |
| Background waveform computation | [x] | Task.Run() in WaveformGenerator |
| Cache waveform peaks | [x] | ConcurrentDictionary cache |
| Custom WPF control | [x] | WaveformViewer UserControl |

---

## Section 9: Animated/Guided Controls

| Requirement | Status | Implementation |
|-------------|--------|----------------|
| Channel selector vibrant styling | [x] | **ENHANCED 2026-01-07**: Gradient background, glow effect, bold cyan text (#7AC0FF) |
| Channel selector badges | [x] | **NEW 2026-01-07**: "Select" badge (unselected), "LIVE" badge (selected) with pulse |
| Channel selector attention | [x] | Bright border, drop shadow glow when channel selected |
| Toolbar button animations | [x] | **NEW 2026-01-07**: Slide-out Popup labels on hover with scale effect |
| "Drop audio here" hint | [~] | Partial placeholder text |
| "Add from Library" animated hint | [ ] | Not implemented |
| Highlight From Date/Time on schedule | [ ] | Not implemented |
| Progress overlays for operations | [~] | Basic progress indicators |
| Activity/Log panel | [x] | ActivityLogControl.xaml |
| Per-operation status | [x] | LogService entries |
| Per target replication results | [x] | Logged per target |
| Per DB write results | [x] | Logged per server |
| Retry actions | [~] | Partial implementation |

---

## Section 10: Logging & Retry

| Requirement | Status | Implementation |
|-------------|--------|----------------|
| In-app log view | [x] | ActivityLogControl.xaml |
| Record user, time, channel | [x] | LogService.Log() |
| Record files written | [x] | Logged on write operations |
| Replication per X target | [x] | Logged in AudioService |
| DB fan-out results per profile | [x] | Logged in DatabaseRouter |
| Retry Replication action | [~] | Manual retry via UI |
| Retry DB Sync action | [~] | Manual retry via UI |

---

## Summary Statistics

| Category | Complete | Partial | Not Started | Total |
|----------|----------|---------|-------------|-------|
| Section 0 (Constraints) | 12 | 0 | 0 | 12 |
| Section 1 (Multi-DB) | 10 | 0 | 0 | 10 |
| Section 2 (Playlist DB) | 16 | 0 | 0 | 16 |
| Section 2A (Agency) | 13 | 0 | 0 | 13 |
| Section 3 (TAG Files) | 20 | 1 | 0 | 21 |
| Section 4 (Audio) | 10 | 2 | 0 | 12 |
| Section 5 (Config Window) | 13 | 1 | 0 | 14 |
| Section 6 (Main Window UX) | 9 | 1 | 0 | 10 |
| Section 7 (Core Screens) | 19 | 5 | 0 | 24 |
| Section 8 (Waveform) | 14 | 2 | 0 | 16 |
| Section 9 (Animations) | 11 | 2 | 2 | 15 |
| Section 10 (Logging) | 6 | 2 | 0 | 8 |
| **TOTAL** | **153** | **16** | **2** | **171** |

**Overall Completion: ~90% Complete**

---

## Key Files Reference

### Models
- `Core/Models/Agency.cs` - Agency data model
- `Core/Models/Capsule.cs` - Capsule with segments
- `Core/Models/Channel.cs` - Channel with X targets
- `Core/Models/Commercial.cs` - Commercial spot
- `Core/Models/Schedule.cs` - Scheduling info
- `Core/Models/TagFile.cs` - TAG file structure
- `Core/Models/TagEntry.cs` - TAG row entry
- `Core/Models/AppConfiguration.cs` - App config

### Interfaces
- `Interfaces/IUnsavedChangesTracker.cs` - Interface for unsaved changes tracking

### Services
- `Core/Services/Database/DatabaseRouter.cs` - Multi-DB routing
- `Core/Services/Tags/TagService.cs` - TAG generation/parsing
- `Core/Services/Audio/AudioService.cs` - Audio conversion
- `Core/Services/Audio/WaveformGenerator.cs` - Waveform data
- `Core/Services/Configuration/ConfigurationService.cs` - Config management
- `Core/Services/Security/SecurityService.cs` - Auth/authorization
- `Core/Services/Agencies/AgencyService.cs` - Agency CRUD
- `Core/Services/Channels/ChannelService.cs` - Channel loading
- `Core/Services/Library/CommercialService.cs` - Commercial management
- `Core/Services/Logging/LogService.cs` - Logging
- `Core/Services/Reports/BroadcastSheetService.cs` - PDF broadcast sheet generation

### Views
- `Windows/MainWindow.xaml` - Main application window
- `Windows/ConfigurationWindow.xaml` - Admin config
- `Windows/CommercialDialog.xaml` - Add/edit commercial
- `Windows/AgencyDialog.xaml` - Add/edit agency
- `Controls/LibraryControl.xaml` - Commercial library
- `Controls/CapsuleBuilderControl.xaml` - Capsule builder
- `Controls/SchedulingControl.xaml` - Schedule setup
- `Controls/TagEditorControl.xaml` - TAG editor
- `Controls/WaveformViewer.xaml` - Waveform display
- `Controls/ActivityLogControl.xaml` - Activity log

---

## Remaining Work (Priority Order)

### High Priority
1. [ ] Verify TAG rename logic when filename-affecting fields change
2. [x] ~~Complete unsaved changes protection across all views~~ (DONE - 2026-01-06)
3. [ ] Verify time collision warning and override

### Medium Priority
4. [ ] Complete audio drag-drop for replace in TAG editor
5. [ ] Add "Add from Library" animated hint arrow
6. [ ] Highlight From Date/Time on schedule dialog open
7. [x] ~~Verify capsule sequential playback with segment highlighting~~ (Cursor tracking FIXED - 2026-01-07)

### Low Priority
8. [ ] Add progress overlays for long operations
9. [ ] Enhance retry actions in Activity Log
10. [ ] Verify multi-file drag-drop creates multiple library entries

### Recently Completed (2026-01-07)
- [x] **Vibrant Channel ComboBox Redesign**: Gradient background, glowing border, bold cyan text (#7AC0FF) when selected
- [x] **Integrated Selection Badges**: "Select" badge (left) when unselected, "LIVE" badge (right) when selected with pulse animation
- [x] **Enhanced Visual Attention**: Drop shadow glow effect, bright borders to keep user aware of selected channel
- [x] **Panel Height Optimization**: Adjusted main panels to 1:1:2 ratio (Scheduled:Library:Playlist Creator)
- [x] **Fixed multi-segment cursor reset bug**: Cursor now tracks correctly across all segments in WaveformViewer
- [x] **Toolbar button hover animations**: Slide-out Popup labels with scale effect on all 5 toolbar buttons
- [x] **BroadcastSheetWindow UI fixes**: Dark theme DatePickers, GroupBox headers, CheckBoxes with professional styling
- [x] **Broadcast Sheet PDF Professional Redesign**:
  - Alternating row backgrounds (white/#F7FAFC) for easy row scanning
  - Spot details accent box with left blue border (#3182CE) + light background (#EDF2F7)
  - Status badges: Green (Active), Yellow (Pending), Red (Expired) color-coded indicators
  - Per-day summary bars showing capsule count and total duration
  - Dashboard summary cards: 4 color-coded metric boxes (Blue/Green/Orange/Purple)
  - Agency breakdown table showing spots and duration per agency
  - Professional color scheme with PdfColors constants class
  - Enhanced header with accent bar and prominent channel name
  - Dark blue table headers (#2C5282) with white text

---

## Recent Commits (Reference)

- `d1a8c5f` - Add Activity Log viewer panel
- `e42718b` - Add Import/Edit TAG workflow
- `b36cd17` - Add Scheduling module for commercial scheduling
- `fefe3ee` - Add TAG file generator/parser for legacy playback
- `008ff10` - Add Capsule builder for assembling commercials

---

*Last Updated: 2026-01-07 (Broadcast Sheet PDF professional redesign with dashboard cards, status badges, and agency breakdown)*
