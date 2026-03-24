# 🚀 Universal Media Downloader - Full-Stack Application

<div align="center">

![.NET](https://img.shields.io/badge/.NET-8-512BD4?style=for-the-badge&logo=dotnet)
![Vue.js](https://img.shields.io/badge/Vue.js-3-4FC08D?style=for-the-badge&logo=vuedotjs)
![SQLite](https://img.shields.io/badge/SQLite-07405E?style=for-the-badge&logo=sqlite)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-512BD4?style=for-the-badge&logo=dotnet)
![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)

**A high-performance, cross-platform media downloader with real-time progress tracking**

[![Features](https://img.shields.io/badge/Features-%E2%9C%94-blue)](#features)
[![Installation](https://img.shields.io/badge/Installation-%F0%9F%9B%A0-yellow)](#installation)
[![Screenshots](https://img.shields.io/badge/Screenshots-%F0%9F%93%B8-orange)](#screenshots)
[![API](https://img.shields.io/badge/API-%F0%9F%94%8D-green)](#api-endpoints)

</div>

---

## ✨ Features

### 🎯 **Core Functionality**

- **Multi-platform support**: YouTube, Facebook, Instagram, TikTok, Twitter, Reddit, Vimeo, etc.
- **Format selection**: Choose video/audio quality (1080p, 720p, audio-only)
- **Real-time progress**: Live download speed, percentage, and ETA
- **Pause/Resume**: Stop and continue downloads anytime
- **Parallel downloads**: Multiple simultaneous downloads supported

<br/>

### 🔧 **Technical Features**

- **Background cleanup**: Automatic removal of completed (3h) and stuck (6h) downloads
- **Process management**: Cross-platform process tree suspension/resume (Windows/Linux/macOS)
- **Database persistence**: SQLite storage with Entity Framework Core
- **Log management**: Rotating logs with size/age limits
- **Authentication**: Simple user-based session management
- **Responsive UI**: Mobile-friendly Vue.js 3 interface

<br/>

### ⚡ **Performance Optimizations**

- Parallel processing for cleanup operations
- Memory-efficient collections (ConcurrentBag, Buffer pools)
- Thread-safe job tracking
- Efficient artifact cleanup with regex patterns

<br/>

### 💾 Downloads Storage Location

- Your downloaded media files will be stored on default Video directory of your OS.

<br/>

---

## 🏗️ Architecture

```
┌───────────────────────────────────────────────┐
│              Vue.js Frontend                  │
│              (Vue 3 + JavaScript)             │
└────────────────────────┬──────────────────────┘
                         │ HTTP / HTTPS
┌────────────────────────▼──────────────────────┐
│           ASP.NET Core Backend                │
│               (.NET 8 + C#)                   │
├───────────────────────────────────────────────┤
│   Download Service   │   Cleanup Service      │
│   Process Control    │   Database Layer       │
└────────────────────────┬──────────────────────┘
                         │─────│
                               │
                         External APIs
┌───────────────────────────────▼────────────────────────────┐
│                    External Services                       │
│          yt-dlp • FFmpeg • Node •Deno • Platform APIs      │
└────────────────────────────────────────────────────────── ─┘
```

<br/>

## 📦 Installation

### **Prerequisites**
- [.NET 8 SDK](https://dotnet.microsoft.com/download)

- ### These will be installed when run the Script located at Downloader_Backend_(Both_Stacks_Here)/Downloader_Tools_Win.ps1 and Downloader_Tools_linux_Mac.sh
- [Node.js 18+] (for frontend)
- [yt-dlp] (auto-downloaded)
- [FFmpeg] (auto-downloaded)
- [Deno] (auto-download)


<br/>

### **Quick Start**

```bash
# 1. Clone the repository
git clone https://github.com/AR1Ablock/Universal_Media_Downloader_Tool.git
cd media-downloader

# 2. Backend setup
cd Downloader_Backend
./Downloader_Tools_linux_Mac.sh (for linux and mac)
powershell.exe Downloader_Tools_Win.ps1 (Windows) 
dotnet restore
dotnet run

# 3. Frontend setup (in new terminal)
cd ../frontend
npm install
npm run dev

# 4. FullStack setup (Backend directory has both Frontend + Backend)
cd Downloader_Backend
./Downloader_Tools_linux_Mac.sh (for linux and mac)
powershell.exe Downloader_Tools_Win.ps1 (Windows) 
dotnet restore
dotnet run


# 5. Access the application
# Frontend: http://localhost:5173
# Backend: http://localhost:5050
```

<br/>

## 📡 API Endpoints

| Method | Endpoint                            | Description                   |
| ------ | ----------------------------------- | ----------------------------- |
| `POST` | `/Downloader/formats`               | Get available formats for URL |
| `POST` | `/Downloader/download`              | Start download                |
| `GET`  | `/Downloader/progress`              | Get download progress         |
| `POST` | `/Downloader/pause`                 | Pause download                |
| `POST` | `/Downloader/resume`                | Resume download               |
| `POST` | `/Downloader/resume-fresh`          | Restart download              |
| `POST` | `/Downloader/delete-ui`             | Remove from UI                |
| `POST` | `/Downloader/delete-file`           | Delete files                  |
| `GET`  | `/Downloader/download-file/{jobId}` | Download completed file       |
| `GET`  | `/Downloader/OS_Check`              | Check OS and download status  |

<br/>

## 📱 Platform Support

| Platform |  Backend |  Frontend  |          Download Engine         |
| -------- | -------- | ---------- | -------------------------------- |
| Windows  | ✅ Full  | ✅ Full   | ✅ yt-dlp + FFmpeg + Node + Deno |
| Linux    | ✅ Full  | ✅ Full   | ✅ yt-dlp + FFmpeg + Node + Deno |
| macOS    | ✅ Full  | ✅ Full   | ✅ yt-dlp + FFmpeg + Node + Deno |

<br/>

## 📁 Project Structure
```
media-downloader/
├── Downloader_Backend/
│   ├── Controllers/
│   │   └── DownloaderController.cs
│   ├── Logic/
│   │   ├── Cleanup_Service.cs
│   │   ├── Download_Persistence.cs
│   │   ├── Process_Controller_For_Pause_Resume.cs
│   │   ├── Utility.cs
│   │   └── Port_Killer.cs
│   ├── Model/
│   │   └── Download_Model.cs
│   ├── Data/
│   │   └── DownloadContext.cs
│   └── Program.cs
|   └── Downloader_Tools_linux_Mac.sh
|   └── Downloader_Tools_Win.ps1
├── frontend/
│   ├── src/
│   │   ├── components/
│   │   │   ├── Downloads.vue
│   │   │   └── Login.vue
│   │   ├── App.vue
│   │   ├── main.js
│   │   └── style.css
│   └── package.json
├── tools/
│   ├── yt-dlp (auto-downloaded)
│   └── ffmpeg (auto-downloaded)
├── Logs/ (auto-created)
├── Downloads.db (auto-created)
└── README.md
```

<br/>

## Process Control

| Platform       | Control Method                              |
| -------------- | ------------------------------------------- |
| Windows        | Thread-level suspension via WinAPI          |
| Linux / macOS  | Signal-based control (`SIGSTOP`, `SIGCONT`) |
| Cross-platform | Process tree management                     |

<br/>

## 🚀 Performance

| Metric                   | Value               | Notes                             |
| ------------------------ | ------------------- | --------------------------------- |
| Max Concurrent Downloads | `8`                 | Configurable in `CleanupService`  |
| Memory Usage             | `200–400 MB`        | Depends on active downloads       |
| Database Size            | `~1 MB / 1000 jobs` | SQLite compression                |
| Startup Time             | `2–3 seconds`       | Cold start with DB initialization |
| API Response Time        | `< 100 ms`          | Typical for simple requests       |

<br/>

## 🔒 Security Features

| Feature           | Description                            |
| ----------------- | -------------------------------------- |
| Input Validation  | URL sanitization and format validation |
| Process Isolation | Download processes run isolated        |
| File Cleanup      | Automatic removal of temporary files   |
| Log Rotation      | Prevents disk exhaustion               |
| Rate Limiting     | Built into yt-dlp integration          |

<br/>

## 📝 Acknowledgments

| Technology   | Purpose                         |
| ------------ | ------------------------------- |
| yt-dlp       | Core downloading engine         |
| FFmpeg       | Media processing and conversion |
| Node         | Used by Frontend and yt-dlp     |
| Deno         | Used by yt-dlp                  |
| ASP.NET Core | Backend framework               |
| Vue.js       | Frontend framework              |

<br/>
<br/>

## 🎨 Screenshots

---

[<img src="https://github.com/user-attachments/assets/02738774-c9bf-4c58-adc8-983d856a430b" width="30%">](https://github.com/user-attachments/assets/02738774-c9bf-4c58-adc8-983d856a430b)
[<img src="https://github.com/user-attachments/assets/3bad5596-b8ed-481c-a0fc-de64c5e7af7f" width="30%">](https://github.com/user-attachments/assets/3bad5596-b8ed-481c-a0fc-de64c5e7af7f)
[<img src="https://github.com/user-attachments/assets/fcb5b545-24e7-4a54-9431-5b05c2564bb2" width="30%">](https://github.com/user-attachments/assets/fcb5b545-24e7-4a54-9431-5b05c2564bb2)

[<img src="https://github.com/user-attachments/assets/1192ac16-bd81-475e-b3dd-82123b8b4ab2" width="30%">](https://github.com/user-attachments/assets/1192ac16-bd81-475e-b3dd-82123b8b4ab2)
[<img src="https://github.com/user-attachments/assets/7fe72dea-3e80-40d3-8eea-80304f0439f8" width="30%">](https://github.com/user-attachments/assets/7fe72dea-3e80-40d3-8eea-80304f0439f8)
[<img src="https://github.com/user-attachments/assets/95abe77f-267a-4dbe-ab5c-cfb2d03faaee" width="30%">](https://github.com/user-attachments/assets/95abe77f-267a-4dbe-ab5c-cfb2d03faaee)

[<img src="https://github.com/user-attachments/assets/b117170d-d17f-4fdc-ae7e-aad92fab2bf4" width="30%">](https://github.com/user-attachments/assets/b117170d-d17f-4fdc-ae7e-aad92fab2bf4)
[<img src="https://github.com/user-attachments/assets/e563eb91-50f6-4eaa-9641-c22128fbeb12" width="30%">](https://github.com/user-attachments/assets/e563eb91-50f6-4eaa-9641-c22128fbeb12)

---

<br/>

<div align="center">

⭐ Star this repository if you find it useful!
<br/>
Built using .NET 8 & Vue.js 3

</div>
