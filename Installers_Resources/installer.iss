[Setup]
AppName=Media Downloader
AppVersion=1.0
DefaultDirName={pf}\MediaDownloader
DefaultGroupName=Media Downloader
OutputBaseFilename=MediaDownloaderInstaller
Compression=lzma
SolidCompression=yes
SetupIconFile=publish\wwwroot\favicon.png

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\Media Downloader"; Filename: "{app}\Downloader_Backend"; IconFilename: "{app}\wwwroot\favicon.png"
Name: "{commondesktop}\Media Downloader"; Filename: "{app}\Downloader_Backend"; IconFilename: "{app}\wwwroot\favicon.png"
