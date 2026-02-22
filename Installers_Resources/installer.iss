[Setup]
AppName=Media Downloader
AppVersion=100.10
ArchitecturesInstallIn64BitMode=x64
DefaultDirName={autopf}\MediaDownloader
DefaultGroupName=Media Downloader
OutputBaseFilename=MediaDownloaderInstaller
Compression=lzma
SolidCompression=yes
SetupIconFile=publish\favicon.ico

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs
Source: "publish\MediaDownloader.xml"; DestDir: "{app}"

[Icons]
Name: "{group}\Media Downloader"; Filename: "{app}\Downloader_Backend.exe"; IconFilename: "{app}\favicon.ico"
Name: "{commondesktop}\Media Downloader"; Filename: "{app}\Downloader_Backend.exe"; IconFilename: "{app}\favicon.ico"

[Run]
Filename: "schtasks"; \
  Parameters: "/Create /TN MediaDownloader /XML ""{app}\MediaDownloader.xml"" /F"; \
  Flags: runhidden
