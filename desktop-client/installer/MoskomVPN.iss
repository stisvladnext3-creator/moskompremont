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
SetupIconFile={#PayloadDir}\brand\moskom-vpn.ico
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autodesktop}\МосКомпРемонт VPN"; Filename: "{app}\MoskomVPN.exe"; IconFilename: "{app}\brand\moskom-vpn.ico"; AppUserModelID: "MosKomRemont.VPN"
Name: "{autoprograms}\МосКомпРемонт VPN"; Filename: "{app}\MoskomVPN.exe"; IconFilename: "{app}\brand\moskom-vpn.ico"; AppUserModelID: "MosKomRemont.VPN"
[Run]
Filename: "{app}\MoskomVPN.exe"; Description: "Открыть МосКомпРемонт VPN"; Flags: nowait; Check: LaunchAfterInstall
[Messages]
WelcomeLabel1=МосКомпРемонт VPN
WelcomeLabel2=Приложение для подключения к вашему VPN.%n%nRussia уже в комплекте. После установки скопируйте ссылку из личного кабинета и нажмите «+».%n%nНажмите «Установить» — приложение установится и откроется автоматически.
[Code]
function OpenProcess(Access: LongWord; Inherit: LongWord; PID: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function InitializeSetup(): Boolean;
var
  PID: Integer;
  Handle: THandle;
  WaitResult: LongWord;
begin
  Result := True;
  PID := StrToIntDef(ExpandConstant('{param:MOSKOMPID|0}'), 0);
  if PID > 0 then begin
    Handle := OpenProcess($00100000, 0, PID);
    if Handle <> 0 then begin
      WaitResult := WaitForSingleObject(Handle, 30000);
      CloseHandle(Handle);
      if WaitResult <> 0 then begin
        MsgBox('Завершите МосКомпРемонт VPN и повторите обновление.', mbError, MB_OK);
        Result := False;
      end;
    end;
  end;
end;

function LaunchAfterInstall(): Boolean;
begin
  Result := (not WizardSilent) or (ExpandConstant('{param:MOSKOMUPDATE|0}') = '1');
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpWelcome then
    WizardForm.NextButton.Caption := 'Установить';
end;
