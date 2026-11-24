Unicode true
!include "MUI2.nsh"
!ifndef APP_VERSION
!error "APP_VERSION required"
!endif
!ifndef SOURCE_DIR
!error "SOURCE_DIR required"
!endif
!ifndef OUTPUT_FILE
!error "OUTPUT_FILE required"
!endif
!ifndef UNINSTALL_FILES
!error "UNINSTALL_FILES required"
!endif
Name "Forterm Avalonia ${APP_VERSION}"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\Forterm Avalonia\${APP_VERSION}"
RequestExecutionLevel user
SetCompressor /SOLID zlib
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\Forterm.exe"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"
VIProductVersion "${APP_NUMERIC}"
VIAddVersionKey /LANG=2052 "ProductName" "Forterm Avalonia"
VIAddVersionKey /LANG=2052 "FileDescription" "Forterm Avalonia 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Forterm contributors"
Section "Forterm Avalonia"
 SetShellVarContext current
 SetOutPath "$INSTDIR"
 File /r "${SOURCE_DIR}\*"
 WriteUninstaller "$INSTDIR\Uninstall.exe"
 CreateDirectory "$SMPROGRAMS\Forterm Avalonia ${APP_VERSION}"
 CreateShortCut "$SMPROGRAMS\Forterm Avalonia ${APP_VERSION}\Forterm.lnk" "$INSTDIR\Forterm.exe"
 CreateShortCut "$SMPROGRAMS\Forterm Avalonia ${APP_VERSION}\Xshell 入口.lnk" "$INSTDIR\xshell.exe"
 CreateShortCut "$SMPROGRAMS\Forterm Avalonia ${APP_VERSION}\Xftp 入口.lnk" "$INSTDIR\xftp.exe"
 CreateShortCut "$SMPROGRAMS\Forterm Avalonia ${APP_VERSION}\卸载.lnk" "$INSTDIR\Uninstall.exe"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermAvalonia-${APP_VERSION}" "DisplayName" "Forterm Avalonia ${APP_VERSION}"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermAvalonia-${APP_VERSION}" "DisplayVersion" "${APP_VERSION}"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermAvalonia-${APP_VERSION}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
 WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermAvalonia-${APP_VERSION}" "NoModify" 1
 WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermAvalonia-${APP_VERSION}" "NoRepair" 1
SectionEnd
Section "Uninstall"
 SetShellVarContext current
 !include "${UNINSTALL_FILES}"
 Delete "$INSTDIR\Uninstall.exe"
 RMDir "$INSTDIR"
 Delete "$SMPROGRAMS\Forterm Avalonia ${APP_VERSION}\Forterm.lnk"
 Delete "$SMPROGRAMS\Forterm Avalonia ${APP_VERSION}\Xshell 入口.lnk"
 Delete "$SMPROGRAMS\Forterm Avalonia ${APP_VERSION}\Xftp 入口.lnk"
 Delete "$SMPROGRAMS\Forterm Avalonia ${APP_VERSION}\卸载.lnk"
 RMDir "$SMPROGRAMS\Forterm Avalonia ${APP_VERSION}"
 DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermAvalonia-${APP_VERSION}"
 ; Only the exact packaged files are removed. User metadata, vault and older builds are preserved.
SectionEnd
