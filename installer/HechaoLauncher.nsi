Unicode true

!ifndef APP_VERSION
  !define APP_VERSION "0.9.1"
!endif

!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\artifacts\publish\win-x64"
!endif

!ifndef OUTPUT_DIR
  !define OUTPUT_DIR "..\artifacts\installer"
!endif

!include "MUI2.nsh"

!define APP_NAME "赫朝启动器"
!define APP_EXE "Hechao.Launcher.exe"
!define APP_REGISTRY_KEY "Software\Hechao\Launcher"
!define UNINSTALL_REGISTRY_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\HechaoLauncher"

Name "${APP_NAME}"
OutFile "${OUTPUT_DIR}\Hechao-Launcher-Setup-${APP_VERSION}-win-x64.exe"
InstallDir "$LOCALAPPDATA\Programs\Hechao Launcher"
InstallDirRegKey HKCU "${APP_REGISTRY_KEY}" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 64
SetDatablockOptimize on
SetOverwrite on
BrandingText "赫朝"

Icon "..\src\Hechao.Launcher\Assets\hechao-launcher.ico"
UninstallIcon "..\src\Hechao.Launcher\Assets\hechao-launcher.ico"

VIProductVersion "${APP_VERSION}.0"
VIFileVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=2052 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=2052 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "FileDescription" "赫朝 Minecraft 服务器启动器安装程序"
VIAddVersionKey /LANG=2052 "CompanyName" "赫朝"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Copyright © 2026 赫朝"

!define MUI_ABORTWARNING
!define MUI_ICON "..\src\Hechao.Launcher\Assets\hechao-launcher.ico"
!define MUI_UNICON "..\src\Hechao.Launcher\Assets\hechao-launcher.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_LINK "访问 hechao.world"
!define MUI_FINISHPAGE_LINK_LOCATION "https://hechao.world"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

LangString MainSectionName ${LANG_SIMPCHINESE} "赫朝启动器（必需）"
LangString MainSectionName ${LANG_ENGLISH} "Hechao Launcher (required)"
LangString DesktopSectionName ${LANG_SIMPCHINESE} "桌面快捷方式"
LangString DesktopSectionName ${LANG_ENGLISH} "Desktop shortcut"

Section "$(MainSectionName)" MainSection
  SectionIn RO
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File /r /x "*.pdb" "${PUBLISH_DIR}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr HKCU "${APP_REGISTRY_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_REGISTRY_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_REGISTRY_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_REGISTRY_KEY}" "Publisher" "赫朝"
  WriteRegStr HKCU "${UNINSTALL_REGISTRY_KEY}" "URLInfoAbout" "https://hechao.world"
  WriteRegStr HKCU "${UNINSTALL_REGISTRY_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "${UNINSTALL_REGISTRY_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_REGISTRY_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_REGISTRY_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_REGISTRY_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_REGISTRY_KEY}" "NoRepair" 1

  CreateDirectory "$SMPROGRAMS\赫朝启动器"
  CreateShortcut "$SMPROGRAMS\赫朝启动器\赫朝启动器.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$SMPROGRAMS\赫朝启动器\卸载赫朝启动器.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

Section /o "$(DesktopSectionName)" DesktopSection
  SetShellVarContext current
  CreateShortcut "$DESKTOP\赫朝启动器.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}"
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete "$DESKTOP\赫朝启动器.lnk"
  RMDir /r "$SMPROGRAMS\赫朝启动器"

  Delete "$INSTDIR\${APP_EXE}"
  RMDir /r "$INSTDIR\Assets"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"

  DeleteRegKey HKCU "${UNINSTALL_REGISTRY_KEY}"
  DeleteRegKey HKCU "${APP_REGISTRY_KEY}"
SectionEnd
