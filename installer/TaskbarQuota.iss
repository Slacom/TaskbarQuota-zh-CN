; Inno Setup — TaskbarQuotaSetup-{version}-{arch}.exe (PowerToys-style naming)
; CI: pass absolute /DPublishDir (Inno resolves relative paths from this script's folder).

#ifndef MyAppName
  #define MyAppName "TaskbarQuota"
#endif
#ifndef MyAppExeName
  #define MyAppExeName "TaskbarQuota.exe"
#endif
#ifndef MyAppId
  #define MyAppId "{{A7C4E2B1-9F3D-4A8E-B5C6-1D2E3F4A5B6C}"
#endif
#ifndef MyDefaultDir
  #define MyDefaultDir "{autopf}\TaskbarQuota"
#endif
#ifndef MyDefaultGroupName
  #define MyDefaultGroupName "TaskbarQuota"
#endif
#ifndef PublishDir
  #define PublishDir "..\src\TaskbarQuota.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "1.3.2.2"
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
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
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

function AskUserDataRetention: Boolean;
var
  Form: TSetupForm;
  Prompt: TNewStaticText;
  Detail: TNewStaticText;
  KeepCheckBox: TNewCheckBox;
  ContinueButton: TNewButton;
  CancelButton: TNewButton;
  ButtonWidth: Integer;
  ButtonTop: Integer;
begin
  Form := CreateCustomForm(ScaleX(520), ScaleY(220), False, True);
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

    Detail := TNewStaticText.Create(Form);
    Detail.AutoSize := False;
    Detail.Left := Prompt.Left;
    Detail.Top := Prompt.Top + Prompt.Height + ScaleY(8);
    Detail.Width := Prompt.Width;
    Detail.WordWrap := True;
    Detail.Caption := '勾选“保留配置”可在以后重新安装时继续使用当前设置。取消勾选将同时删除当前配置和旧版 WinCheck 配置。';
    Detail.Parent := Form;
    Detail.AdjustHeight;

    KeepCheckBox := TNewCheckBox.Create(Form);
    KeepCheckBox.Left := Prompt.Left;
    KeepCheckBox.Top := Detail.Top + Detail.Height + ScaleY(12);
    KeepCheckBox.Width := Prompt.Width;
    KeepCheckBox.Height := ScaleY(20);
    KeepCheckBox.Caption := '保留配置（%LOCALAPPDATA%\TaskbarQuota）';
    KeepCheckBox.Checked := True;
    KeepCheckBox.Parent := Form;

    ButtonTop := Form.ClientHeight - ScaleY(23) - ScaleY(16);
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

function InitializeUninstall: Boolean;
begin
  KeepUserData := True;
  if IsUninstallSilent() then
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
