[Setup]
AppName=Media Downloader
AppVersion=1.0
DefaultDirName={pf}\MediaDownloader
DefaultGroupName=Media Downloader
OutputBaseFilename=MediaDownloaderInstaller
Compression=lzma
SolidCompression=yes
SetupIconFile=Installers_Resources\favicon.ico

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs
Source: "publish\MediaDownloader.xml"; DestDir: "{app}"

[Icons]
Name: "{group}\Media Downloader"; Filename: "{app}\Downloader_Backend.exe"; IconFilename: "{app}\wwwroot\favicon.png"
Name: "{commondesktop}\Media Downloader"; Filename: "{app}\Downloader_Backend.exe"; IconFilename: "{app}\wwwroot\favicon.png"

[Run]
Filename: "schtasks"; \
  Parameters: "/Create /TN MediaDownloader /XML {app}\MediaDownloader.xml /F"; \
  Flags: runhidden
