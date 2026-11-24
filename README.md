# WpfMqtt1 — Windows 11 WPF MQTT Client

A modern Windows 11 desktop MQTT client built with C#, .NET 8 WPF, **WPF-UI** (Fluent Design with Mica/Acrylic), **MQTTnet v5**, and embedded **SQLite** session history.

Connect to MQTT brokers over TCP, TLS, or WebSockets, manage DPAPI-encrypted broker profiles, subscribe to multiple topics with wildcards, inspect and publish lossless Text/JSON/Hex/Base64 payloads, and record sessions to SQLite for offline browsing, sorting, and CSV/JSON export.

---

## Features

- **Windows 11 Fluent Shell (`WPF-UI`)**
  - `FluentWindow` with Mica / Acrylic / None backdrop, rounded corners, and custom Fluent MQTT app icon in the title bar and taskbar
  - Automatically follows Windows 11 system theme and accent color, plus manual **System / Light / Dark** switching
  - Collapsible `NavigationView` sidebar (**Client**, **Brokers**, **History**, **Settings**) and persistent bottom status bar showing live connection state, active broker URI, quick **Disconnect**, message counter, and live **REC** indicator
  - Window size, position, and maximized state persist across restarts (with multi-monitor bounds validation)
- **Live MQTT Client Workspace (`Client` page)**
  - Single active broker connection with **multiple simultaneous topic subscriptions** (`+` and `#` wildcards), color tags, per-topic message counters, mute/unmute, and one-click topic reuse in the publisher
  - Non-blocking `Channel<T>`-batched virtualized live message feed with search, direction filter (`All` / `Inbound` / `Outbound`), topic filter, auto-scroll, pause/resume, clear, and configurable ring-buffer cap
  - Shared **Payload Inspector** supporting lossless `byte[]` rendering in **Text (UTF-8)**, **JSON** (pretty-printed with validation warnings), **Hex** (16-byte offset + hex + ASCII dump), and **Base64**, with metadata pills, byte size readout, and one-click copy
  - Multi-format **Publisher** supporting Text, JSON (with **Format JSON** helper), Hex, and Base64, QoS `0/1/2`, Retain flag, live validation, and `Ctrl+Enter` publishing
- **MQTT Broker Profile Manager (`Brokers` page)**
  - Create, search, duplicate, edit, and delete broker profiles
  - Transport protocols: **TCP** (`mqtt://`), **TLS / SSL** (`mqtts://`), **WebSocket** (`ws://`), and **WebSocket Secure** (`wss://`) with automatic standard port suggestion (`1883` / `8883` / `80` / `443`)
  - MQTT protocol versions: **v5.0**, **v3.1.1**, and **v3.1.0**
  - Client ID generator, Clean Session / Clean Start, Keep-Alive, Connection Timeout, and Auto-Reconnect (with automatic re-subscription)
  - Local credential protection using **Windows DPAPI** (`DataProtectionScope.CurrentUser`) — plaintext passwords are never written to disk
  - TLS version selection (`System Default`, `TLS 1.2`, `TLS 1.3`) and optional self-signed certificate bypass for local/dev brokers
  - Per-broker default auto-subscribe topic list and built-in **Test Connection** handshake with round-trip latency reporting
- **Offline SQLite Session History (`History` page)**
  - Non-blocking `Channel<T>`-batched SQLite (`WAL` mode) session recorder with manual **Record / Stop Rec** toggle and optional **Auto-record on connect**
  - Fully offline 3-column session browser: filter sessions by broker/name and date range, rename or delete sessions, and inspect messages without connecting to a broker
  - Virtualized `DataGrid` with SQLite-backed ascending/descending sorting on **Date / Time** and **Topic**, plus message search, direction filter, and topic filter
  - One-click **Export CSV** (RFC 4180) and **Export JSON** (with inline parsed JSON payloads and lossless Base64)
- **Settings & Maintenance (`Settings` page)**
  - Theme & backdrop controls, default publish QoS & payload format, live buffer limit, auto-record toggle, SQLite database size readout, **Compact (VACUUM)**, **Open Data Folder**, and built-in Keyboard Shortcuts reference

---

## Keyboard Shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+1` .. `Ctrl+4` | Switch between **Client**, **Brokers**, **History**, and **Settings** |
| `Ctrl+F` | Focus the search / filter input on the current page |
| `Ctrl+Enter` | Publish message from the Client payload editor |
| `Ctrl+R` | Start / Stop SQLite session recording (Client page) |
| `Ctrl+P` | Pause / Resume live message feed (Client page) |
| `Ctrl+L` | Clear live message feed (Client page) |
| `Ctrl+S` | Save currently edited broker profile (Brokers page) |
| `F5` | Refresh recorded sessions and messages (History page) |

---

## Tech Stack

| Area | Technology |
| --- | --- |
| Runtime / UI | `.NET 8` (`net8.0-windows`, with major roll-forward to .NET 9/10/11), WPF |
| Fluent Design | [WPF-UI](https://wpfui.lepo.co/) (`4.3.0`) & `WPF-UI.DependencyInjection` |
| MQTT Engine | [MQTTnet](https://github.com/dotnet/MQTTnet) (`5.2.0.1603`) |
| MVVM & DI | `CommunityToolkit.Mvvm` (`8.4.2`), `Microsoft.Extensions.Hosting` (`8.0.1`) |
| Credential Storage | Windows DPAPI (`System.Security.Cryptography.ProtectedData`) |
| Offline Storage | `Microsoft.Data.Sqlite` (`8.0.11`, WAL mode) |
| Testing | `xUnit` (`WpfMqtt1.Tests`) |

---

## Getting Started

### Prerequisites
- Windows 11 (or Windows 10 1809+ for fallback surfaces)
- [.NET 8 SDK](https://dotnet.microsoft.com/) (or newer, e.g. .NET 11 SDK)

### Clone & Run
```powershell
git clone https://github.com/krazykyran/WPF-MQTTClient1.git
cd WPF-MQTTClient1
dotnet run --project src/WpfMqtt1
```

### Build & Run Tests
```powershell
dotnet build WpfMqtt1.slnx
dotnet test WpfMqtt1.slnx
```

---

## Data & Security

Application configuration, offline history, and diagnostics are stored per-user under:

```text
%LocalAppData%\WpfMqtt1\
├── settings.json    # Theme, backdrop, window bounds, and client/recording preferences
├── brokers.json     # Broker profiles (passwords stored as DPAPI-encrypted Base64 blobs)
├── history.db       # SQLite offline session database (WAL mode)
└── logs\errors.log  # Unhandled exception log (created only if an error occurs)
```

---

## Project Structure

```text
WpfMqtt1.slnx
├── PLAN.md
├── README.md
├── src/WpfMqtt1/
│   ├── Assets/         # Custom multi-resolution Windows 11 Fluent MQTT icon (.ico & .png)
│   ├── Converters/     # Value converters for status indicators, enums, hex brushes, and numbers
│   ├── Data/           # SQLite session repository (schema, WAL mode, batched writes, sorted queries)
│   ├── Models/         # Broker profiles, app settings, subscriptions, messages, sessions
│   ├── Services/       # MQTTnet wrapper, payload codecs, DPAPI protector, recorder, export, error log
│   ├── ViewModels/     # CommunityToolkit.Mvvm ViewModels
│   └── Views/          # MainWindow, reusable PayloadInspectorControl, and Pages
└── tests/WpfMqtt1.Tests/
```
