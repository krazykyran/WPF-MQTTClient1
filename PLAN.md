# WpfMqtt1 — Windows 11 WPF MQTT Client Plan

## 1. Feature Summary & Scope

- **Connection Model**: Single active broker connection at a time with **multiple simultaneous topic subscriptions** (full MQTT wildcard support: `+` and `#`) and quick topic reuse in the publisher.
- **Payloads**: Lossless `byte[]`-backed message pipeline supporting **UTF-8 Text**, **JSON** (pretty-printing + validation), **Hex Dump**, and **Base64** for both viewing and publishing.
- **Broker Profiles & Security**: Manage multiple broker configurations supporting **TCP / TLS** (`mqtt://`, `mqtts://`) and **WebSockets** (`ws://`, `wss://`), **MQTT v5.0 / v3.1.1 / v3.1.0**, username/password encrypted locally with **Windows DPAPI**, and optional self-signed certificate bypass for local/dev brokers.
- **Offline Session History (SQLite)**: Recording toggle per session (plus optional auto-record on connect), backed by a dedicated **History / Sessions** page to browse past sessions, search/filter messages, inspect payloads, and export to **CSV** or **JSON**.
- **Windows 11 Look & Feel**: Built on **WPF-UI** (`FluentWindow` with Mica backdrop, `NavigationView` sidebar, Windows 11 system accent & theme following, plus a manual **System / Light / Dark** switcher).

---

## 2. Technology Stack & NuGet Packages

- **Target Framework**: `net11.0-windows` (`.NET 11.0.100` SDK)
- **Packages**:
  - `WPF-UI` (`4.3.0`) & `WPF-UI.DependencyInjection` (`4.3.0`) — Fluent Design controls, `FluentWindow`, Mica backdrop, theme/accent watcher, `NavigationView`, `Snackbar`, `ContentDialog`
  - `MQTTnet` (`5.2.0.1603`) — Modern async MQTT client supporting MQTT 3.1.0, 3.1.1, and 5.0 over TCP, TLS, and WebSockets
  - `CommunityToolkit.Mvvm` (`8.4.2`) — Source-generated `[ObservableProperty]` and `[RelayCommand]` MVVM plumbing
  - `Microsoft.Extensions.Hosting` (`11.0.0`) — Dependency injection, app lifecycle management, and structured logging
  - `Microsoft.Data.Sqlite` (`11.0.0`) — Lightweight, fast embedded SQLite access (WAL mode, no EF Core startup overhead)
  - `System.Security.Cryptography.ProtectedData` (`11.0.0`) — Windows DPAPI encryption for broker passwords at rest
- **Test Project**: `WpfMqtt1.Tests` (`xUnit`) for payload codecs, DPAPI protection, MQTT options building, and SQLite repository verification.

---

## 3. Project Structure (Clean MVVM + DI)

```text
WpfMqtt1.slnx
├── src/WpfMqtt1/
│   ├── App.xaml / App.xaml.cs          # HostBuilder DI setup, WPF-UI theme/accent watcher
│   ├── Models/
│   │   ├── BrokerProfile.cs            # Connection settings, transport, TLS, MQTT version
│   │   ├── TopicSubscription.cs        # Filter, QoS, color tag, muted flag, msg count
│   │   ├── MqttLogMessage.cs           # Direction, timestamp, topic, QoS, retain, raw byte[]
│   │   ├── RecordingSession.cs         # Session metadata (id, broker, start/end, count)
│   │   └── AppSettings.cs              # Theme, backdrop, recording defaults, buffer limit
│   ├── Services/
│   │   ├── MqttClientService.cs        # MQTTnet v5 wrapper, reconnects, pub/sub events
│   │   ├── PayloadCodecService.cs      # Text / JSON pretty-print / Hex dump / Base64 encode & decode
│   │   ├── SecretProtectorService.cs   # DPAPI CurrentUser encrypt/decrypt for passwords
│   │   ├── BrokerProfileStore.cs       # Persists broker profiles to %LocalAppData%\WpfMqtt1\brokers.json
│   │   ├── SettingsService.cs          # Persists UI/app settings to settings.json + applies WPF-UI theme
│   │   ├── SessionRecorderService.cs   # Channel-backed async batch writer to SQLite
│   │   └── ExportService.cs            # CSV and JSON export for live or historical sessions
│   ├── Data/
│   │   └── SqliteSessionRepository.cs  # Schema init (WAL), CRUD, paged search/filter queries
│   ├── ViewModels/
│   │   ├── MainWindowViewModel.cs      # Navigation, connection status bar, global snackbar
│   │   ├── ClientViewModel.cs          # Live connect/sub/pub/feed/inspector/recording controls
│   │   ├── BrokersViewModel.cs         # Broker CRUD, validation, Test Connection action
│   │   ├── HistoryViewModel.cs         # Offline session browser, search, inspector, export
│   │   └── SettingsViewModel.cs        # Theme, Mica backdrop, logging & DB maintenance
│   ├── Views/
│   │   ├── MainWindow.xaml             # FluentWindow (Mica) + NavigationView + StatusBar
│   │   ├── Pages/
│   │   │   ├── ClientPage.xaml
│   │   │   ├── BrokersPage.xaml
│   │   │   ├── HistoryPage.xaml
│   │   │   └── SettingsPage.xaml
│   │   └── Controls/
│   │       └── PayloadInspectorControl.xaml  # Shared Text/JSON/Hex/Base64 viewer (reused in Client & History)
│   └── Converters/                     # Direction badge, QoS color, byte size, visibility converters
└── tests/WpfMqtt1.Tests/
```

---

## 4. UI / UX Design (Windows 11 + WPF-UI)

### Application Shell (`MainWindow`)
- `ui:FluentWindow` with `WindowBackdropType="Mica"` and `ExtendsContentIntoTitleBar="True"`.
- Left collapsible `ui:NavigationView` (`PaneDisplayMode="Left"`) with 4 destinations:
  1. **Client** (Live Workspace)
  2. **Brokers** (Server Profiles)
  3. **History** (Offline SQLite Sessions)
  4. **Settings** (Footer item)
- Bottom persistent status bar showing connection state pill (Disconnected / Connecting / Connected / Reconnecting), active broker URI, message counter, and an animated **REC** indicator when SQLite recording is active.

### Page 1: Client (Live Workspace)
- **Top Toolbar**: Broker selector `ComboBox`, **Connect / Disconnect** primary button, **Record to SQLite** `ToggleSwitch` (with optional session title), and quick status badge.
- **Left Panel (Subscriptions)**:
  - Quick subscribe bar (Topic input + QoS `0/1/2` + Subscribe button).
  - Active subscriptions list showing color indicator, topic filter, QoS badge, unread/total message count, quick **"Use in Publisher"** click action, mute toggle, and unsubscribe button.
- **Center Panel (Live Message Feed)**:
  - Filter bar: search text (matches topic or decoded payload), direction filter (`All` / `In` / `Out`), topic filter, **Auto-scroll** toggle, **Pause** toggle, **Clear** button.
  - Virtualized list displaying timestamp (`HH:mm:ss.fff`), direction arrow (`↓ IN` / `↑ OUT`), topic, QoS & Retain pills, byte size, and single-line payload preview.
- **Right Panel (Message Inspector — `PayloadInspectorControl`)**:
  - Metadata header (exact timestamp, topic, direction, QoS, Retain, byte length, MQTT v5 Content-Type if present).
  - View mode segmented tabs: **Text (UTF-8)**, **JSON** (indented pretty-print with invalid-JSON fallback notice), **Hex** (classic 16-byte offset + hex bytes + ASCII sidebar), **Base64**.
  - One-click **Copy Payload** and **Copy Topic** buttons.
- **Bottom Docked Panel (Publisher)**:
  - Topic `ComboBox` (editable, auto-populated with subscribed & recently published topics), QoS selector (`0/1/2`), **Retain** toggle, and Payload Format selector (`Text`, `JSON`, `Hex`, `Base64`).
  - Multi-line payload editor with live validation indicator (for JSON/Hex/Base64), a **Format JSON** helper button, and **Publish** button (`Ctrl+Enter` shortcut).

### Page 2: Brokers (Server Manager)
- **Master-Detail Layout**:
  - Left pane: Broker profile cards showing name, `protocol://host:port`, MQTT version badge, and actions (**Add**, **Duplicate**, **Delete**, **Connect**).
  - Right pane (scrollable WPF-UI `CardExpander` / grouped settings cards):
    - **General**: Profile Name, Transport (`TCP`, `TLS`, `WebSocket`, `WebSocket Secure`), Hostname, Port (auto-suggests `1883`, `8883`, `80`, `443` when switching transport), WebSocket Path (shown only for WS/WSS).
    - **MQTT Options**: Protocol Version (`v5.0`, `v3.1.1`, `v3.1.0`), Client ID (with randomize button), Clean Session / Clean Start toggle, Keep-Alive seconds, Connection Timeout, Auto-Reconnect toggle.
    - **Authentication & TLS**: Username, Password (`ui:PasswordBox`, DPAPI-encrypted on save), TLS Version (`System Default`, `TLS 1.2`, `TLS 1.3`), and **"Allow untrusted / self-signed certificates"** toggle (with an inline caution `InfoBar`).
    - **Default Topics** (optional): Saved list of topics to auto-subscribe upon connecting to this broker.
    - **Footer Actions**: **Test Connection** (performs a quick connect/disconnect handshake and reports latency or exact broker error), **Save**, **Revert**.

### Page 3: History / Sessions (Offline SQLite Viewer)
- **Left Pane (Session List)**:
  - Search & filter by broker name and date range.
  - Each session item displays session name, broker name/host, start time, duration, and recorded message count.
  - Actions: Rename session, Delete session (with `ContentDialog` confirmation), **Export CSV**, **Export JSON**.
- **Right Pane (Offline Log & Inspector)**:
  - Reuses the same filterable, virtualized message list + `PayloadInspectorControl` (`Text` / `JSON` / `Hex` / `Base64`) as the live Client page—works completely offline without any broker connection.

### Page 4: Settings
- **Appearance**: Theme (`Follow Windows System`, `Light`, `Dark`) and Window Backdrop (`Mica`, `Acrylic`, `None`).
- **Session Recording & Storage**: Toggle **Auto-record on connect**, live feed in-memory ring buffer cap (default `5,000` messages), SQLite database path & file size readout, **Open Data Folder** button, and **Compact Database (`VACUUM`)** button.
- **Defaults**: Default publish QoS and default payload format.

---

## 5. Storage & SQLite Design

All app data is stored under `%LocalAppData%\WpfMqtt1\`:
- `settings.json` & `brokers.json` — Kept separate from the SQLite database so users can archive, share, or wipe session logs without losing broker configs. Broker passwords are stored as Base64 DPAPI-encrypted blobs (`DataProtectionScope.CurrentUser`).
- `history.db` — SQLite database configured with `PRAGMA journal_mode = WAL;` and `PRAGMA foreign_keys = ON;`:

```sql
CREATE TABLE Sessions (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL,
    BrokerName      TEXT    NOT NULL,
    BrokerAddress   TEXT    NOT NULL,
    MqttVersion     TEXT    NOT NULL,
    StartedAtUtc    TEXT    NOT NULL,
    EndedAtUtc      TEXT    NULL,
    MessageCount    INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE Messages (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId       INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    TimestampUtc    TEXT    NOT NULL,
    Direction       INTEGER NOT NULL, -- 0 = Inbound, 1 = Outbound
    Topic           TEXT    NOT NULL,
    Qos             INTEGER NOT NULL,
    Retain          INTEGER NOT NULL,
    PayloadBytes    BLOB    NOT NULL,
    PayloadFormat   TEXT    NULL,     -- Hint / MQTT v5 ContentType
    PayloadPreview  TEXT    NOT NULL  -- First ~256 chars UTF-8 or hex summary for fast search/list
);

CREATE INDEX IX_Messages_Session_Time  ON Messages(SessionId, TimestampUtc);
CREATE INDEX IX_Messages_Session_Topic ON Messages(SessionId, Topic);
```

---

## 6. Performance & Reliability Considerations

- **Non-Blocking High-Throughput Pipeline**: Incoming MQTTnet callbacks never touch the UI thread directly. Messages are pushed into two `System.Threading.Channels.Channel<MqttLogMessage>` queues:
  1. **UI Batching Loop**: Drains messages every ~50 ms onto the WPF `Dispatcher` in batches and trims the live `ObservableCollection` at the configured ring-buffer cap so the UI stays smooth even at hundreds of messages/second.
  2. **SQLite Batch Writer**: When recording is active, batches inserts into a single SQLite transaction every 50 messages or 250 ms (and flushes cleanly on disconnect/stop/shutdown).
- **Clean Shutdown**: `OnExit` cancels background tasks, flushes pending SQLite batches, marks the active session's `EndedAtUtc`, and gracefully disconnects `IMqttClient`.
- **Thread-Safe Reconnection**: Re-subscribes all active `TopicSubscription` entries automatically after a reconnect if Clean Session is enabled.

---

## 7. Phased Implementation Roadmap

1. **Phase 1 — Solution Scaffold & WPF-UI Shell**
   - Create solution (`WpfMqtt1.slnx`), WPF project (`net11.0-windows`), and xUnit test project.
   - Configure `Microsoft.Extensions.Hosting`, WPF-UI `FluentWindow` with Mica backdrop, `NavigationView`, `SystemThemeWatcher`, and placeholder pages + status bar.
   - *Verify*: App launches cleanly on Windows 11 with Mica, theme switching, and navigation working.
2. **Phase 2 — Core Services, Settings & Broker Manager**
   - Implement `SecretProtectorService` (DPAPI), `SettingsService`, `BrokerProfileStore`, and `MqttClientService` (TCP/TLS/WS/WSS, MQTT v3.1.0/v3.1.1/v5.0, self-signed cert toggle).
   - Build **BrokersPage** (CRUD, validation, Test Connection) and **SettingsPage**.
   - *Verify*: Unit tests pass for DPAPI & MQTT options builder; brokers persist across restarts and Test Connection succeeds.
3. **Phase 3 — Live MQTT Client & Payload Inspector**
   - Implement `PayloadCodecService` (Text, JSON pretty-print/validation, Hex dump, Base64) and reusable `PayloadInspectorControl`.
   - Build **ClientPage** (connect/disconnect, multi-topic subscribe with wildcards, batched virtualized message feed, search/filter/pause, and multi-format publisher).
   - *Verify*: Unit tests pass for payload codecs; live subscribe/publish works end-to-end with Text, JSON, Hex, and Base64 payloads.
4. **Phase 4 — SQLite Session Recording, History Page & Export**
   - Implement `SqliteSessionRepository`, `SessionRecorderService` (batched channel writes), and `ExportService` (CSV/JSON).
   - Wire the live Recording toggle in `ClientPage` and build **HistoryPage** with offline browsing, filtering, inspection, deletion, and export.
   - *Verify*: Repository & export unit tests pass; recorded sessions survive app restart and display/export accurately offline.
5. **Phase 5 — Polish & Verification**
   - Keyboard shortcuts (`Ctrl+Enter` to publish, `Ctrl+F` to focus filter), empty states, WPF-UI `Snackbar` notifications for errors/actions, window state persistence, and full build/test pass.
