; Inno Setup — TaskbarQuotaSetup-{version}-{arch}.exe (PowerToys-style naming)
; CI: pass absolute /DPublishDir (Inno resolves relative paths from this script's folder).

#ifndef MyAppName
  #define MyAppName "TaskbarQuota-zh-CN"
#endif
#ifndef MyAppExeName
  #define MyAppExeName "TaskbarQuota.exe"
#endif
#ifndef MyAppId
  #define MyAppId "{{D4B19A5E-6C62-4D84-9F1B-0A7C58E2F3D4}"
#endif
#ifndef MyAppIdValue
  #define MyAppIdValue "D4B19A5E-6C62-4D84-9F1B-0A7C58E2F3D4"
#endif
#ifndef MyDefaultDir
  #define MyDefaultDir "{autopf}\TaskbarQuota-zh-CN"
#endif
#ifndef MyDefaultGroupName
  #define MyDefaultGroupName "TaskbarQuota-zh-CN"
#endif
#ifndef PublishDir
  #define PublishDir "..\src\TaskbarQuota.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "1.3.2.3"
#endif
#ifndef TargetArch
  #define TargetArch "x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#ifndef MyUserDataRoot
  #define MyUserDataRoot "{localappdata}"
#endif

#if TargetArch == "arm64"
  #define ArchAllowed "arm64"
  #define ArchInstallMode "arm64"
#else
  #define ArchAllowed "x64compatible"
  #define ArchInstallMode "x64compatible"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Zied Kallel
AppPublisherURL=https://github.com/zioder/TaskbarQuota
AppSupportURL=https://github.com/zioder/TaskbarQuota/issues
AppUpdatesURL=https://github.com/zioder/TaskbarQuota/releases
DefaultDirName={#MyDefaultDir}
DefaultGroupName={#MyDefaultGroupName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=TaskbarQuotaSetup-{#MyAppVersion}-{#TargetArch}
SetupIconFile=..\src\TaskbarQuota.App\Assets\TaskBarQuota.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed={#ArchAllowed}
ArchitecturesInstallIn64BitMode={#ArchInstallMode}
MinVersion=10.0.19041
PrivilegesRequired=lowest

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; Parameters: "/SILENT /TASKBARQUOTA_INTERACTIVE_UNINSTALL"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  KeepUserData: Boolean;

function IsUninstallSilent: Boolean;
var
  I: Integer;
  Param: String;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    Param := UpperCase(ParamStr(I));
    if (Param = '/SILENT') or (Param = '/VERYSILENT') then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function IsInteractiveUninstall: Boolean;
var
  I: Integer;
  Param: String;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    Param := UpperCase(ParamStr(I));
    if Param = '/TASKBARQUOTA_INTERACTIVE_UNINSTALL' then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function AskUserDataRetention: Boolean;
var
  Form: TSetupForm;
  Prompt: TNewStaticText;
  KeepCheckBox: TNewCheckBox;
  ContinueButton: TNewButton;
  CancelButton: TNewButton;
  ButtonWidth: Integer;
  ButtonTop: Integer;
begin
  Form := CreateCustomForm(ScaleX(300), ScaleY(150), False, True);
  try
    Form.Caption := ExpandConstant('{#MyAppName}') + ' 卸载';

    Prompt := TNewStaticText.Create(Form);
    Prompt.AutoSize := False;
    Prompt.Left := ScaleX(18);
    Prompt.Top := ScaleY(16);
    Prompt.Width := Form.ClientWidth - ScaleX(36);
    Prompt.WordWrap := True;
    Prompt.Caption := '确定要卸载 ' + ExpandConstant('{#MyAppName}') + ' 吗？';
    Prompt.Parent := Form;
    Prompt.AdjustHeight;

    KeepCheckBox := TNewCheckBox.Create(Form);
    KeepCheckBox.Left := Prompt.Left;
    KeepCheckBox.Top := Prompt.Top + Prompt.Height + ScaleY(8);
    KeepCheckBox.Width := Prompt.Width;
    KeepCheckBox.Height := ScaleY(20);
    KeepCheckBox.Caption := '保留配置';
    KeepCheckBox.Checked := True;
    KeepCheckBox.Parent := Form;

    ButtonTop := KeepCheckBox.Top + KeepCheckBox.Height + ScaleY(8);
    Form.ClientHeight := ButtonTop + ScaleY(23) + ScaleY(16);
    ButtonWidth := Form.CalculateButtonWidth(['继续卸载', '取消']);

    CancelButton := TNewButton.Create(Form);
    CancelButton.Parent := Form;
    CancelButton.Caption := '取消';
    CancelButton.Left := Form.ClientWidth - ButtonWidth - ScaleX(18);
    CancelButton.Top := ButtonTop;
    CancelButton.Width := ButtonWidth;
    CancelButton.Height := ScaleY(23);
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;

    ContinueButton := TNewButton.Create(Form);
    ContinueButton.Parent := Form;
    ContinueButton.Caption := '继续卸载';
    ContinueButton.Left := CancelButton.Left - ButtonWidth - ScaleX(8);
    ContinueButton.Top := ButtonTop;
    ContinueButton.Width := ButtonWidth;
    ContinueButton.Height := ScaleY(23);
    ContinueButton.ModalResult := mrOk;
    ContinueButton.Default := True;

    Form.ActiveControl := KeepCheckBox;
    Result := Form.ShowModal() = mrOk;
    if Result then
      KeepUserData := KeepCheckBox.Checked;
  finally
    Form.Free();
  end;
end;

procedure ConfigureUninstallCommand;
var
  UninstallKey: String;
  UninstallCommand: String;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' +
    '{#MyAppIdValue}' + '}_is1';
  UninstallCommand := '"' + ExpandConstant('{app}\unins000.exe') +
    '" /SILENT /TASKBARQUOTA_INTERACTIVE_UNINSTALL';
  RegWriteStringValue(HKEY_CURRENT_USER, UninstallKey, 'UninstallString', UninstallCommand);
end;

procedure DeinitializeSetup;
begin
  // Inno writes its own uninstall registration after the normal setup steps. Write this last so
  // the interactive retention prompt is also used by Apps & Features, not only the Start menu link.
  ConfigureUninstallCommand();
end;

function InitializeUninstall: Boolean;
begin
  KeepUserData := True;
  if IsUninstallSilent() and not IsInteractiveUninstall() then
  begin
    // Non-interactive uninstallers keep user data by default; there is no checkbox to make this choice.
    Result := True;
    Exit;
  end;

  Result := AskUserDataRetention();
end;

procedure DeleteUserData;
var
  CurrentDataDir: String;
  LegacyDataDir: String;
begin
  CurrentDataDir := ExpandConstant('{#MyUserDataRoot}\TaskbarQuota');
  LegacyDataDir := ExpandConstant('{#MyUserDataRoot}\WinCheck');
  DelTree(CurrentDataDir, True, True, True);
  DelTree(LegacyDataDir, True, True, True);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usUninstall) and (not KeepUserData) then
    DeleteUserData();
end;
