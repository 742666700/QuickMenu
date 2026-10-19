; QuickMenu installer. Built by scripts/build.ps1 -Package from the framework-dependent (-small) publish folder:
;   makensis /DVERSION=2.0.5 /DSOURCE_DIR=<publish folder> /DICON=<.ico> /DOUTPUT=<setup .exe> QuickMenu.nsi
; Installs per user (no admin). If the .NET 10 Desktop Runtime is missing it is installed first; that one step
; asks for administrator rights. With /DRUNTIME_FILE=<Microsoft's windowsdesktop-runtime-win-x64.exe> the runtime
; installer is carried inside (offline setup); without it the runtime is downloaded from Microsoft when needed.

Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma

!ifndef VERSION
  !error "Pass /DVERSION=x.y.z"
!endif
!ifndef SOURCE_DIR
  !error "Pass /DSOURCE_DIR=<publish folder>"
!endif
!ifndef ICON
  !error "Pass /DICON=<path to quick-menu.ico>"
!endif
!ifndef OUTPUT
  !define OUTPUT "QuickMenu-Setup-${VERSION}-x64.exe"
!endif

!define EXE "QuickMenu.exe"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickMenu"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
; Always the newest .NET 10 Desktop Runtime for x64, served by Microsoft.
!define RUNTIME_URL "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
!define RUNTIME_PAGE "https://dotnet.microsoft.com/download/dotnet/10.0"
!define RUNTIME_DIR "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App"

!include MUI2.nsh
!include LogicLib.nsh
!include FileFunc.nsh
!include WordFunc.nsh

Name "$(AppName)"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\QuickMenu"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
RequestExecutionLevel user
BrandingText "$(AppName) ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=0 "ProductName" "QuickMenu"
VIAddVersionKey /LANG=0 "FileDescription" "QuickMenu Setup"
VIAddVersionKey /LANG=0 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=0 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=0 "LegalCopyright" "QuickMenu contributors"

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!define MUI_COMPONENTSPAGE_NODESC
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "$(RunNow)"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

LangString AppName ${LANG_SIMPCHINESE} "快捷菜单"
LangString AppName ${LANG_ENGLISH} "QuickMenu"
LangString SecApp ${LANG_SIMPCHINESE} "快捷菜单（必需）"
LangString SecApp ${LANG_ENGLISH} "QuickMenu (required)"
LangString SecDesktop ${LANG_SIMPCHINESE} "桌面快捷方式"
LangString SecDesktop ${LANG_ENGLISH} "Desktop shortcut"
LangString RunNow ${LANG_SIMPCHINESE} "立即运行快捷菜单"
LangString RunNow ${LANG_ENGLISH} "Run QuickMenu now"
LangString RuntimeFound ${LANG_SIMPCHINESE} "已检测到 .NET 10 桌面运行时，跳过安装。"
LangString RuntimeFound ${LANG_ENGLISH} "The .NET 10 Desktop Runtime is already installed."
LangString RuntimeDownloading ${LANG_SIMPCHINESE} "未检测到 .NET 10 桌面运行时，正在从微软下载……"
LangString RuntimeDownloading ${LANG_ENGLISH} "The .NET 10 Desktop Runtime is missing. Downloading it from Microsoft..."
LangString RuntimeUnpacking ${LANG_SIMPCHINESE} "未检测到 .NET 10 桌面运行时，正在使用安装包内置的运行时……"
LangString RuntimeUnpacking ${LANG_ENGLISH} "The .NET 10 Desktop Runtime is missing. Using the copy inside this setup..."
LangString RuntimeInstalling ${LANG_SIMPCHINESE} "正在安装 .NET 10 桌面运行时（需要管理员权限，请在弹出的窗口中选择“是”）……"
LangString RuntimeInstalling ${LANG_ENGLISH} "Installing the .NET 10 Desktop Runtime (needs administrator rights; choose Yes when asked)..."
LangString RuntimeFailed ${LANG_SIMPCHINESE} "未能安装 .NET 10 桌面运行时，快捷菜单需要它才能运行。$\r$\n$\r$\n可能是网络无法连接微软服务器，或没有允许管理员权限。$\r$\n$\r$\n是否打开微软的下载页面手动安装？安装后再运行本安装程序即可。"
LangString RuntimeFailed ${LANG_ENGLISH} "The .NET 10 Desktop Runtime could not be installed, and QuickMenu needs it.$\r$\n$\r$\nThe download may have been blocked, or administrator rights were declined.$\r$\n$\r$\nOpen Microsoft's download page to install it by hand? Then run this setup again."
LangString RuntimeUntrusted ${LANG_SIMPCHINESE} "下载到的运行时安装包没有有效的微软签名，或版本不是 10，已拒绝运行。"
LangString RuntimeUntrusted ${LANG_ENGLISH} "The downloaded runtime installer is not validly signed by Microsoft as version 10, so it was not run."
LangString RuntimeAbort ${LANG_SIMPCHINESE} "缺少 .NET 10 桌面运行时，安装已停止。"
LangString RuntimeAbort ${LANG_ENGLISH} "Setup stopped: the .NET 10 Desktop Runtime is missing."
LangString CloseApp ${LANG_SIMPCHINESE} "快捷菜单正在运行，需要先关闭它才能继续安装。$\r$\n$\r$\n点击“确定”关闭并继续。"
LangString CloseApp ${LANG_ENGLISH} "QuickMenu is running and must be closed to continue.$\r$\n$\r$\nChoose OK to close it and continue."
LangString RemovingOld ${LANG_SIMPCHINESE} "正在移除之前安装在别处的旧版本："
LangString RemovingOld ${LANG_ENGLISH} "Removing an older copy installed elsewhere:"

Var Downloader

; The .NET launcher looks for the Desktop Runtime in the shared Program Files location.
!macro HasRuntime _result
  StrCpy ${_result} 0
  ${If} ${FileExists} "${RUNTIME_DIR}\10.*"
    StrCpy ${_result} 1
  ${EndIf}
!macroend

Function EnsureRuntime
  !insertmacro HasRuntime $0
  ${If} $0 == 1
    DetailPrint "$(RuntimeFound)"
    Return
  ${EndIf}
  InitPluginsDir
!ifdef RUNTIME_FILE
  ; Offline setup: Microsoft's runtime installer travels inside this file.
  DetailPrint "$(RuntimeUnpacking)"
  SetOutPath "$PLUGINSDIR"
  File "/oname=dotnet-desktop-runtime.exe" "${RUNTIME_FILE}"
!else
  DetailPrint "$(RuntimeDownloading)"
  ; curl ships with Windows 10 1803 and later; the 64-bit copy is reached through Sysnative from this 32-bit setup.
  StrCpy $Downloader "$WINDIR\Sysnative\curl.exe"
  ${IfNot} ${FileExists} $Downloader
    StrCpy $Downloader "$SYSDIR\curl.exe"
  ${EndIf}
  nsExec::ExecToLog '"$Downloader" -L --fail --retry 3 --connect-timeout 30 -o "$PLUGINSDIR\dotnet-desktop-runtime.exe" "${RUNTIME_URL}"'
  Pop $0
  ${IfNot} ${FileExists} "$PLUGINSDIR\dotnet-desktop-runtime.exe"
    ; Fallback for systems without curl, or where curl cannot use the proxy settings.
    nsExec::ExecToLog `powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing -Uri '${RUNTIME_URL}' -OutFile '$PLUGINSDIR\dotnet-desktop-runtime.exe'"`
    Pop $0
  ${EndIf}
!endif
  ${If} ${FileExists} "$PLUGINSDIR\dotnet-desktop-runtime.exe"
    ; It runs with administrator rights, so only if it really is Microsoft's .NET 10 runtime installer: validly signed by
    ; Microsoft and named as the 10.x Windows Desktop Runtime, the same check the build makes before packing it into the
    ; offline setup.
    ${WordReplace} "$PLUGINSDIR\dotnet-desktop-runtime.exe" "'" "''" "+" $1
    nsExec::ExecToLog `powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$$f='$1'; $$s=Get-AuthenticodeSignature -LiteralPath $$f; $$i=(Get-Item -LiteralPath $$f).VersionInfo; if ($$s.Status -eq 'Valid' -and $$s.SignerCertificate.Subject -match 'O=Microsoft Corporation' -and $$i.ProductVersion -like '10.*' -and $$i.ProductName -like 'Microsoft Windows Desktop Runtime 10.*') { exit 0 } else { exit 1 }"`
    Pop $0
    ${If} $0 == 0
      DetailPrint "$(RuntimeInstalling)"
      ExecShellWait "runas" "$PLUGINSDIR\dotnet-desktop-runtime.exe" "/install /quiet /norestart" SW_SHOWNORMAL
    ${Else}
      DetailPrint "$(RuntimeUntrusted)"
      Delete "$PLUGINSDIR\dotnet-desktop-runtime.exe"
    ${EndIf}
  ${EndIf}
  !insertmacro HasRuntime $0
  ${If} $0 == 1
    Return
  ${EndIf}
  MessageBox MB_YESNO|MB_ICONEXCLAMATION "$(RuntimeFailed)" /SD IDNO IDNO +2
  ExecShell "open" "${RUNTIME_PAGE}"
  Abort "$(RuntimeAbort)"
FunctionEnd

; An upgrade has to replace QuickMenu.exe, which is locked while the menu waits in the tray.
Function CloseRunningApp
  nsExec::ExecToStack '"$SYSDIR\cmd.exe" /c tasklist /FI "IMAGENAME eq ${EXE}" /NH | "$SYSDIR\find.exe" /I "${EXE}"'
  Pop $0
  Pop $1
  ${If} $0 == 0
    MessageBox MB_OKCANCEL|MB_ICONINFORMATION "$(CloseApp)" /SD IDOK IDOK +2
    Abort
    nsExec::Exec '"$SYSDIR\taskkill.exe" /IM ${EXE} /F'
    Pop $0
    Sleep 1000
  ${EndIf}
FunctionEnd

; The program files this setup puts in its folder. Settings live elsewhere (%LOCALAPPDATA%\QuickMenu) and stay.
!macro RemoveProgramFiles _dir
  Delete "${_dir}\${EXE}"
  Delete "${_dir}\LICENSE.txt"
  Delete "${_dir}\使用说明.md"
  Delete "${_dir}\user-guide.md"
  RMDir /r "${_dir}\Assets"
  ; Marker left in the folder by the 1.0 installer.
  Delete "${_dir}\.quick-menu-install"
!macroend

; One copy per user: a copy installed earlier in another folder is removed, so only this one is left to start,
; update or uninstall. Takes the folder on the stack; it is left alone unless QuickMenu.exe is there.
Function RemoveCopyAt
  Exch $R9
  ${If} $R9 != ""
  ${AndIf} $R9 != $INSTDIR
  ${AndIf} ${FileExists} "$R9\${EXE}"
    DetailPrint "$(RemovingOld) $R9"
    !insertmacro RemoveProgramFiles $R9
    Delete "$R9\Uninstall.exe"
    RMDir "$R9"
  ${EndIf}
  Pop $R9
FunctionEnd

Section "-Runtime"
  Call EnsureRuntime
SectionEnd

Section "$(SecApp)" SecApp
  SectionIn RO
  Call CloseRunningApp
  ; The folder the last setup recorded and the default folder, when this install goes somewhere else; then files an
  ; older version left here that this one no longer has.
  ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "InstallLocation"
  Push $0
  Call RemoveCopyAt
  Push "$LOCALAPPDATA\Programs\QuickMenu"
  Call RemoveCopyAt
  !insertmacro RemoveProgramFiles $INSTDIR
  SetOutPath "$INSTDIR"
  File /r "${SOURCE_DIR}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  ; Shortcuts are named in the setup's language; one left in the other language by an earlier setup goes too.
  Delete "$SMPROGRAMS\快捷菜单.lnk"
  Delete "$SMPROGRAMS\QuickMenu.lnk"
  CreateShortCut "$SMPROGRAMS\$(AppName).lnk" "$INSTDIR\${EXE}"
  ; A desktop shortcut from before stays, now pointing at this copy.
  ${If} ${FileExists} "$DESKTOP\快捷菜单.lnk"
  ${OrIf} ${FileExists} "$DESKTOP\QuickMenu.lnk"
    Delete "$DESKTOP\快捷菜单.lnk"
    Delete "$DESKTOP\QuickMenu.lnk"
    CreateShortCut "$DESKTOP\$(AppName).lnk" "$INSTDIR\${EXE}"
  ${EndIf}

  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "$(AppName)"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "QuickMenu"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "URLInfoAbout" "https://github.com/742666700/QuickMenu"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" $0

  ; Someone who had "start with Windows" on keeps it, now pointing at this installed copy.
  ReadRegStr $0 HKCU "${RUN_KEY}" "QuickMenu"
  ${If} $0 != ""
    WriteRegStr HKCU "${RUN_KEY}" "QuickMenu" '"$INSTDIR\${EXE}" --background'
  ${EndIf}
SectionEnd

Section /o "$(SecDesktop)" SecDesktop
  Delete "$DESKTOP\快捷菜单.lnk"
  Delete "$DESKTOP\QuickMenu.lnk"
  CreateShortCut "$DESKTOP\$(AppName).lnk" "$INSTDIR\${EXE}"
SectionEnd

; Only what this setup put there is removed; settings in %LOCALAPPDATA%\QuickMenu stay for a reinstall.
Section "Uninstall"
  nsExec::Exec '"$SYSDIR\taskkill.exe" /IM ${EXE} /F'
  Pop $0
  Sleep 800
  ReadRegStr $0 HKCU "${RUN_KEY}" "QuickMenu"
  ${If} $0 == '"$INSTDIR\${EXE}" --background'
    DeleteRegValue HKCU "${RUN_KEY}" "QuickMenu"
  ${EndIf}
  Delete "$SMPROGRAMS\快捷菜单.lnk"
  Delete "$SMPROGRAMS\QuickMenu.lnk"
  Delete "$DESKTOP\快捷菜单.lnk"
  Delete "$DESKTOP\QuickMenu.lnk"
  !insertmacro RemoveProgramFiles $INSTDIR
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
SectionEnd
