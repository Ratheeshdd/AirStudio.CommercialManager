⚠️ **FROZEN SPEC — DO NOT INFER OR MODIFY WITHOUT EXPLICIT USER CONFIRMATION**

This document is an authoritative, frozen implementation specification.
Claude Code must follow it *exactly as written*.
No assumptions, no inferred features, no schema changes unless explicitly instructed by the user.

---

# Claude Code Prompt — AirStudio Commercial Manager (WPF .NET 8) — Implementation Plan (Frozen Spec)

You are Claude Code acting as a senior C# / WPF / MySQL architect and implementation engineer.

## Objective
Build **AirStudio Commercial Manager**, a **C# WPF (.NET 8)** desktop application for **All India Radio studios** to manage, edit, and schedule commercials per channel. It is a full replacement for a legacy Commercial Manager and must remain **legacy-playback compatible** via **TAG files**.

This prompt contains the **frozen requirements** (no guessing). Implement accordingly.

---

# 0) Non‑negotiable constraints

## 0.1 .NET / UI
- **C# WPF .NET 8.0**
- Dark blue theme aligned with **AirStudio.Playback** (reuse styling patterns; create a clean, professional style system).
- Fully async UI. No blocking operations on UI thread.

## 0.2 Database access library
- **Use `MySqlConnector` only** for all database operations (no MySql.Data, no EF).

## 0.3 Security & Admin
- Only allowed users can run app; unauthorized users must see message and exit.
- Configuration UI is visible only to **Windows Local Administrators** group membership.

## 0.4 Machine‑wide persistent configuration
- All configuration changes persist for **all users on that machine** (ProgramData).
- Include one-click **Export Settings** and **Import Settings**.
- Export includes DB passwords encrypted using AES with **fixed passphrase `"air"`** (no prompt).

## 0.5 Channels source
- Channels are loaded from DB:
  - Database: `air_virtual_studio`
  - Table: `setup_channels`
  - Column: `Channel`

## 0.6 Channel locations (X targets)
- Each channel has **one or more X root targets** (e.g., `X:\`, `Y:\`).
- Only X roots are stored in settings; app derives:
  - TAG folder: `X:\Commercial Playlist\`
  - Commercial audio folder: `X:\Commercials\`
- A channel is **not usable** unless it has **≥ 1** X root configured.

---

# 1) Multi‑Database Strategy (Reads & Writes)

## 1.1 DB Profiles in config
- Config contains an ordered list of DB profiles (IP/Port/User/Password/SSL/Timeout).
- **Default DB profile** is used for normal operation; operators cannot change it.

## 1.2 Read strategy (load/search)
- Reads/search use **parallel-first-success** strategy:
  - Query Default + next fallback(s) in parallel (cap concurrency to 2–3).
  - Return first successful response; cancel others.
  - If all fail → show disconnected state.

## 1.3 Write strategy (scheduling writes)
- When a commercial schedule is created/updated, it must **write to ALL configured DB servers** (fan-out).
- IDs may differ per server (acceptable).
- Partial DB failures: **warn but allow** (consistent with replication policy).
- Updates are **self‑healing**:
  - Try UPDATE first; if affected rows == 0 → INSERT.

---

# 2) Legacy Playlist DB Contract (Commercial Scheduling)

## 2.1 Playlist table location
- Database name: `air_` + channelName (selected channel)
- Table name: `playlist`

## 2.2 Key types (confirmed)
- `TxTime` is stored as **VARCHAR** (write `"HH:mm:ss"` always)
- `TxDate` is **DATE** (display may show `00:00:00`)
- `Validity` is **DATE**

## 2.3 Commercial row constants
- `Mode = 2`
- `ProgType = "COMMERCIALS"`
- `StartTime = 0` always

## 2.4 Insert fields (minimum required)
Insert a row with:
- `Mode = 2`
- `TxTime = "HH:mm:ss"` (VARCHAR)
- `TxDate = FromDate` (DATE)
- `Validity = ToDate` (DATE)
- `Programme = CapsuleName`
- `Title = FirstCutSpotName` (e.g. `GONG2`)
- `Duration = TotalDurationHHMMSS` (e.g. `00:01:02`)
- `StopTime = TotalSecondsDouble` (e.g. `61.77`)
- `ProgType = "COMMERCIALS"`
- `MainPath = FullTagPath` (e.g. `X:\Commercial Playlist\064350_010126(2)_CAPSULE.TAG`)
- `LoginUser = Windows/DNS login`
- `LastUpdate = NOW()`
- `UserName = entered at scheduling time`
- `MobileNo = entered at scheduling time`

Leave legacy columns blank if applicable:
- `nSetDuration`, `nCode`, `StandbyPath`, `ServerPath`, `nCount`, `PID`, `nIndex`, etc.

## 2.5 Update behavior
- Editing an existing TAG should **update** the existing playlist row (do not delete/reinsert).
- Row identification key for update across servers:
  - `Mode=2 AND TxDate AND TxTime AND MainPath`
- If UPDATE affects 0 rows on a given DB server → INSERT (self-heal).

---


---

# 2A) Agency Master (Per-Channel) — NEW FROZEN SPEC (Mandatory)

Commercial Manager must manage agencies per channel database.

## 2A.1 Agency table
- Database: `air_<channel>`
- Table: `agency`
- Columns:
  - `Code` (AUTO_INCREMENT, primary key)
  - `AgencyName` (**mandatory**)
  - `Address`
  - `PIN`
  - `Phone`
  - `Email`

## 2A.2 Agency CRUD (UI + DB)
- Users must be able to **Add / Edit / Delete** agencies.
- `AgencyName` is required; all other fields optional.
- Deleting an agency:
  - Prefer safety: **block delete** if referenced by any commercial/library item (or scheduled usage), and show a clear message.

## 2A.3 Agency selection in “Add Commercial”
- In the “Add/Edit Commercial” dialog, the user must select an agency:
  - Show **AgencyName** to users, store `Code` (AgencyCode).
  - Selection control must support **search-as-you-type** (typeahead).
  - Preferred UX: editable ComboBox with incremental search and a filtered dropdown list.

## 2A.4 “Add Agency” inline flow (floating window)
- If the user types an agency name that does **not exist**, show a **floating Add Agency window** (modal dialog over the Add Commercial dialog):
  - Pre-fill `AgencyName` with the typed text.
  - Allow entry of Address, PIN, Phone, Email.
  - Save → INSERT into `air_<channel>.agency`.
- When the floating window closes successfully:
  - Refresh agency list (from DB, using read strategy).
  - Automatically select the newly created agency in the Add Commercial dialog.
  - Restore focus to the next required field in Add Commercial.

## 2A.5 Data usage in TAG files
- Agency `Code` is written to TAG **column 9** (AgencyCode) for each cut.


# 3) TAG Files — Legacy Compatibility Contract (Critical)

## 3.1 TAG filename format (locked)
Example: `064350_010126(2)_BEFORE MORNG REG NEWS.TAG`

Format:
```
HHMMSS_DDMMYY(R)_CAPSULE NAME.TAG
```
Rules:
- Capsule name in filename:
  - keep spaces
  - remove invalid filename characters
  - force UPPERCASE
- Extension must be **`.TAG` uppercase**.
- `(R)` equals repeat days **R = (ToDate - FromDate).Days**
  - R=2 means today + next 2 days.

## 3.2 TAG creation rules
- Only **one TAG file** exists even with repeats.
- On edit of an existing TAG: **overwrite in-place** (same filename/path).
- If schedule changes affect filename (ToDate/TxDate/TxTime/CapsuleName), **rename** TAG and update DB `MainPath`.
- No backup copies on rename or overwrite.

## 3.3 TAG row format (locked)
- Tab-separated (`\t`)
- Keep **10 columns**.
- Use TxTime in column 2 (same for all rows per TAG).

Columns (fixed generation):
1) Category:
   - `2` for each commercial cut row
   - `1` for the terminator line
2) TxTime: `"HH:mm:ss"`
3) SpotName (cut name)
4) CapsuleName
5) Duration `"HH:mm:ss"`
6) Flag: always `0`
7) DurationSecondsFloat (precise)
8) AudioPath (absolute) e.g. `X:\Commercials\GONG2.WAV`
9) AgencyCode (from library metadata)
10) SequenceToken: `NN:01:XX` (see below)

## 3.4 Terminator line
- For multi-cut capsules, append a final terminator line:
  - Col1 = `1`
  - Col3/5/8 may repeat first cut's values (legacy-like)
  - Col10 ends with `NN:01:99`
- For single-spot tags:
  - Write **one line only** with Col1=`1` and Col10=`NN:01:99`.

## 3.5 SequenceToken (NN base)
- NN cannot be reliably reverse-engineered.
- Implement deterministic policy:
  - Provide a configurable `SequenceBaseNN` default (e.g., 27) in settings (advanced).
  - For N cuts:
    - lines 1..N: `NN:01:01`, `NN:01:02`, ...
    - terminator: `NN:01:99`
- When importing a TAG, preserve existing NN if parseable; otherwise use default.

---

# 4) Audio Processing (Library + Edit)

## 4.1 Audio standard (final)
Always reconvert any audio to:
- WAV (PCM)
- 16-bit
- **48,000 Hz**
- stereo

## 4.2 Output naming/location
- Output filename is based on **Spot Name**:
  - Keep spaces
  - Remove invalid filename chars
  - UPPERCASE
  - extension `.WAV` uppercase
- Store WAV under each channel X target:
  - `X:\Commercials\<SPOTNAME>.WAV`

## 4.3 Overwrite behavior
- If a WAV with same name exists, overwrite after successful reconversion (atomic swap).
- Partial replication failures are allowed (warn but allow).

## 4.4 Audio drag & drop (required UX)
- Support drag & drop wherever sensible:
  - Drop audio file(s) onto Library grid to add items quickly.
  - Drop audio onto “Replace Audio” area in TAG editor or Capsule builder to replace selected cut.
  - Drop multiple files: auto-create multiple library entries (spot name derived from filename, user can edit before final save).

---

# 5) Configuration Window (Admin Only)

The main window must show a **Configuration button** only for Windows Local Admins.

Configuration window tabs:

## 5.1 Databases tab
- Manage ordered DB profiles (Default + fallbacks).
- Fields: Name, Host/IP, Port, Username, Password, SSL Mode, Timeout.
- Test Connection button using MySqlConnector.
- Set Default profile.
- Password storage:
  - At-rest on machine: DPAPI machine scope.
  - Export: AES with passphrase `"air"`.

## 5.2 Locations tab
- Show channels (from DB) and allow mapping:
  - Channel → list of X root targets (e.g., `X:\`, `Y:\`)
- Validate that X root exists and is writable (test button).
- Channel must have ≥ 1 X root to be usable.

## 5.3 Users tab
- Allowed users allow-list (DOMAIN\\user or MACHINE\\user).
- Only these users can run app. Others exit with message.

## 5.4 Export / Import (single click)
- Export a single JSON file containing config + AES-encrypted DB passwords (passphrase `"air"` fixed).
- Import overwrites machine config after validation and creates .bak backup.

---

# 6) Main Window UX (Frozen)

## 6.1 Startup flow
- App starts with **no channel selected**.
- Channel ComboBox must be **animated** to attract attention until a channel is selected.
- All other operations disabled until a channel is selected and configured (X targets present).

## 6.2 DB profile usage
- Operators always use **Default DB profile** automatically.
- Reads use parallel-first-success across profiles.

## 6.3 Unsaved protection
- If user has unsaved changes (capsule edit or TAG edit), block:
  - channel change
  - window close
  - navigation change
- Prompt: Save / Discard / Cancel.

## 6.4 Collision policy
- If scheduling time collision exists (same TxDate+TxTime Mode=2), warn but allow override.

---

# 7) Core Screens & Workflows

## 7.1 Library screen
- Grid + search (Spot/Title/Agency).
- Add/Edit/Deactivate.
- Drag-and-drop audio to add.
- On add/edit: reconvert to WAV 48k 16-bit stereo; replicate to all X targets.
- Save DB record even if replication is partial (warn but allow).

## 7.2 Capsule Builder
- Build capsule from library cuts.
- Reorder/remove, compute total duration.
- Drag drop from library list to capsule list.
- Show waveform segmentation (see waveform section).
- Button: “Schedule” → opens scheduling dialog.
- Also allow “Save TAG only” (optional).

## 7.3 Scheduling dialog (Updated, final)
- Do **not** prompt for RepeatDays.
- Use:
  - From Date (TxDate)
  - TxTime
  - To Date (Validity)
- Compute R = (ToDate - FromDate).Days
- Require UserName & MobileNo input every scheduling operation.
- For single commercial schedule from library: automatically create capsule name = SpotName.

## 7.4 Import / Edit TAG (self-healing)
- Browse TAGs from `X:\Commercial Playlist\` on primary X root.
- If TAG missing on some targets: on first save, auto-replicate to missing targets.
- Parse TAG → edit cuts → overwrite TAG in place (or rename if filename-affecting changes).
- Save triggers:
  - file write + replication
  - DB update across all servers (self-heal missing rows)
- Audio replace within TAG editor:
  - drag-drop supported
  - reconvert to WAV and overwrite `<SPOTNAME>.WAV` then replicate.

---

# 8) Waveform Viewer (Required UX Upgrade)

## 8.1 General requirements
- Provide a **clean waveform viewer** (dark theme friendly).
- Any selected audio should show waveform quickly (cached).
- For capsules:
  - Show **segmented waveform** in the same viewer:
    - Each segment corresponds to a cut.
    - Different segment colors (theme-aware palette; not garish).
    - Segment separators and labels (SpotName, duration).
- Waveform view must update when:
  - selection changes
  - reordering changes
  - audio replacement occurs

## 8.2 Playback preview (recommended)
- Add minimal preview controls:
  - Play/Pause, Stop
  - Scrub/seek
  - Show current time / total
- For capsule preview:
  - Play sequentially with a single control.
  - Highlight currently playing segment.

## 8.3 Implementation suggestion (allowed)
- Use NAudio for decoding and playback.
- Compute waveform samples in background tasks.
- Cache waveform peaks (e.g., downsample to fixed number of points per second).
- Use a custom WPF control that draws peaks efficiently (DrawingVisual/WriteableBitmap).

---

# 9) Animated / Guided Controls (UX improvements)

Implement subtle, professional animations:
- Channel selector pulse until selected.
- “Next step” guidance:
  - When no library items exist, show “Drop audio here to add commercial.”
  - When capsule empty, show animated hint arrow to “Add from Library.”
  - When schedule dialog opens, highlight From Date/Time.
- Progress overlays for long operations:
  - audio conversion
  - replication
  - multi-DB fan-out
- A unified “Activity / Log” panel:
  - per-operation status
  - per target replication results
  - per DB write results
  - retry actions

---

# 10) Logging & Retry (Must-have)

- Provide in-app log view (recent operations).
- Each operation should record:
  - user, time, channel
  - files written
  - replication per X target (success/failure reason)
  - DB fan-out results per profile
- Provide “Retry Replication” and “Retry DB Sync” actions.

---

# 11) Implementation Deliverables (what to build)

1) WPF solution + project structure:
   - UI project (WPF)
   - Core project (services, models)
   - Data project (repositories, SQL)
2) ProgramData config store + DPAPI secrets store.
3) Export/Import mechanism (AES passphrase `"air"`).
4) Multi-DB router:
   - parallel-first-success reads
   - fan-out writes
   - self-healing update/insert
5) Channel loader from `air_virtual_studio.setup_channels`.
6) Library module (drag-drop, conversion, replication).
7) Waveform viewer + segmented capsule waveform.
8) TAG generator + parser + editor.
9) Scheduling module with From Date/Time + To Date validity.
10) Log/Retry UI.

---

# 12) Notes for Claude Code
- Do not invent additional DB tables unless asked. If library table schema is unknown, implement repository with placeholder SQL + clearly mark needed table/columns and request schema before final wiring.
- All IO and DB must be async.
- Be careful with file locks and atomic replace:
  - write to temp, then move/replace.
- Sanitization:
  - Remove invalid filename chars
  - Preserve spaces
  - UPPERCASE
- Use robust error messages and logs; never crash on missing targets.

---

# 13) Acceptance Criteria (must pass)
- Unauthorized user exits immediately.
- Admin-only config button works based on Windows Local Admins membership.
- Channels list loads from DB and requires X targets configured.
- Library add via drag-drop converts audio to 48k/16-bit/stereo WAV and replicates to all X targets.
- Scheduling generates correct TAG filename and content; writes playlist rows to all DB servers.
- Reads/search work even if default DB down (parallel fallback).
- TAG import/edit overwrites in place; rename logic correct on ToDate/TxDate/TxTime/Capsule changes; DB updated accordingly with self-healing behavior.
- Waveform viewer shows single audio and segmented capsule waveforms with clear UI.

---

## Start Implementation
Proceed to implement the system in a clean architecture, beginning with:
1) Config store + security gate
2) Multi-DB router + channel loader
3) UI skeleton and navigation
4) Library conversion + replication + waveform viewer
