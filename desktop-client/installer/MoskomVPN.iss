#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
[Setup]
AppId={{837F99C2-A13B-4994-B7B0-AE3B4D178D8D}
AppName=МосКомпРемонт VPN
AppVersion={#AppVersion}
AppPublisher=МосКомпРемонт
DefaultDirName={localappdata}\Programs\MosKomRemontVPN
DefaultGroupName=МосКомпРемонт VPN
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
DisableWelcomePage=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=MoskomVPN-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\MoskomVPN.exe
CloseApplications=yes
RestartApplications=no
AppMutex=Local\MosKomRemontVPN.App
SetupLogging=yes
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autodesktop}\МосКомпРемонт VPN"; Filename: "{app}\MoskomVPN.exe"
Name: "{autoprograms}\МосКомпРемонт VPN"; Filename: "{app}\MoskomVPN.exe"
[Run]
Filename: "{app}\MoskomVPN.exe"; Description: "Открыть МосКомпРемонт VPN"; Flags: nowait skipifsilent
[Messages]
WelcomeLabel1=МосКомпРемонт VPN
WelcomeLabel2=Приложение для подключения к вашему VPN.%n%nRussia уже в комплекте. После установки скопируйте ссылку из личного кабинета и нажмите «+».%n%nНажмите «Установить» — приложение установится и откроется автоматически.
[Code]
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpWelcome then
    WizardForm.NextButton.Caption := 'Установить';
end;
