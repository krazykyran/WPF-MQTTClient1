# WpfMqtt1 — Windows 11 WPF MQTT Client

A modern Windows 11 desktop MQTT client built with C#, .NET 11 WPF, **WPF-UI** (Fluent Design with Mica/Acrylic), and **MQTTnet v5**.

Connect to MQTT brokers over TCP, TLS, or WebSockets, manage encrypted broker profiles, subscribe to multiple topics with wildcards, inspect and publish Text/JSON/Hex/Base64 payloads, and record sessions to SQLite for offline inspection.

---

## Features

### Implemented (Phases 1 & 2)
- **Windows 11 Fluent Shell (`WPF-UI`)**
  - `FluentWindow` with Mica / Acrylic / Solid window backdrop support and rounded corners
  - Follows Windows 11 system theme and accent color automatically, plus manual **System / Light / Dark** switching
  - Collapsible `NavigationView` sidebar (**Client**, **Brokers**, **History**, **Settings**) and persistent bottom status bar with live connection state and quick **Disconnect** control
- **MQTT Broker Profile Manager (`Brokers` page)**
  - Create, search, duplicate, edit, and delete broker profiles
  - Transport protocols: **TCP** (`mqtt://`), **TLS / SSL** (`mqtts://`), **WebSocket** (`ws://`), and **WebSocket Secure** (`wss://`) with automatic standard port suggestion (`1883` / `8883` / `80` / `443`)
  - MQTT protocol versions: **v5.0**, **v3.1.1**, and **v3.1.0**
  - Client ID generator, Clean Session / Clean Start, Keep-Alive, Connection Timeout, and Auto-Reconnect options
  - Local credential protection using **Windows DPAPI** (`DataProtectionScope.CurrentUser`) — plaintext passwords are never written to disk
  - TLS version selection (`System Default`, `TLS 1.2`, `TLS 1.3`) and optional self-signed certificate bypass for development brokers
  - Optional default auto-subscribe topic list per broker
  - Built-in **Test Connection** handshake with round-trip latency reporting, plus **Connect / Disconnect** controls
- **Persistent Settings (`Settings` page)**
  - Persisted appearance preferences (theme & window backdrop), default publish QoS, default payload format, live buffer limit, and auto-record toggle
  - Quick link to open the local application data folder

### Planned (Phases 3–5)
- **Live MQTT Client Workspace (Phase 3)**: Multi-topic subscriptions (`+` / `#` wildcards), high-throughput virtualized message feed with search/filter/pause, lossless `byte[]` **Text / JSON / Hex / Base64** payload inspector, and multi-format publisher
- **Offline SQLite Session History (Phase 4)**: Toggleable SQLite session recording (`WAL` mode), offline session browser, message search, payload inspection, and CSV/JSON export
- **Polish (Phase 5)**: Keyboard shortcuts (`Ctrl+Enter` publish, `Ctrl+F` filter), window state persistence, and database maintenance (`VACUUM`)

See [PLAN.md](PLAN.md) for the full architecture and phased implementation roadmap.

---

## Tech Stack

| Area | Technology |
| --- | --- |
| Runtime / UI | `.NET 11` (`net11.0-windows`), WPF |
| Fluent Design | [WPF-UI](https://wpfui.lepo.co/) (`4.3.0`) & `WPF-UI.DependencyInjection` |
| MQTT Engine | [MQTTnet](https://github.com/dotnet/MQTTnet) (`5.2.0.1603`) |
| MVVM & DI | `CommunityToolkit.Mvvm` (`8.4.2`), `Microsoft.Extensions.Hosting` (`11.0.0`) |
| Credential Storage | Windows DPAPI (`System.Security.Cryptography.ProtectedData`) |
| Offline Storage *(Phase 4)* | `Microsoft.Data.Sqlite` |
| Testing | `xUnit` (`WpfMqtt1.Tests`) |

---

## Getting Started

### Prerequisites
- Windows 11 (or Windows 10 1809+ for fallback surfaces)
- [.NET 11 SDK](https://dotnet.microsoft.com/) (`11.0.100` or later)

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

Application configuration and data are stored per-user under:

```text
%LocalAppData%\WpfMqtt1\
├── settings.json   # Theme, backdrop, and client/recording preferences
├── brokers.json    # Broker profiles (passwords stored as DPAPI-encrypted Base64 blobs)
└── history.db      # SQLite offline session database (created when session recording is enabled)
```

---

## Project Structure

```text
WpfMqtt1.slnx
├── PLAN.md
├── README.md
├── src/WpfMqtt1/
│   ├── Converters/     # Value converters for status indicators, enums, and numeric inputs
│   ├── Models/         # Broker profiles, app settings, enums, and MQTT message records
│   ├── Services/       # MQTTnet client wrapper, DPAPI protector, settings & broker persistence, theme watcher
│   ├── ViewModels/     # CommunityToolkit.Mvvm ViewModels
│   └── Views/          # MainWindow (FluentWindow) and Pages (Client, Brokers, History, Settings)
└── tests/WpfMqtt1.Tests/
```
