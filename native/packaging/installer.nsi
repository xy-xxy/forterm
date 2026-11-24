Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"
!ifndef APP_VERSION
!define APP_VERSION "0.2.1"
!endif
!ifndef SOURCE_DIR
!error "SOURCE_DIR must point to the prepared native runtime directory"
!endif
!ifndef OUTPUT_FILE
!error "OUTPUT_FILE is required"
!endif
Name "Forterm Native ${APP_VERSION}"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\Forterm Native"
InstallDirRegKey HKCU "Software\FortermNative" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\forterm-native.exe"
!define MUI_FINISHPAGE_RUN_TEXT "启动 Forterm Native"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"
VIProductVersion "0.2.1.0"
VIAddVersionKey /LANG=2052 "ProductName" "Forterm Native"
VIAddVersionKey /LANG=2052 "FileDescription" "Forterm Native 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Forterm contributors"
Section "Forterm Native" SEC_MAIN
 SetShellVarContext current
 SetOutPath "$INSTDIR"
 File /r "${SOURCE_DIR}\*"
 WriteUninstaller "$INSTDIR\Uninstall.exe"
 CreateDirectory "$SMPROGRAMS\Forterm Native"
 CreateShortCut "$SMPROGRAMS\Forterm Native\Forterm Native.lnk" "$INSTDIR\forterm-native.exe"
 CreateShortCut "$SMPROGRAMS\Forterm Native\卸载.lnk" "$INSTDIR\Uninstall.exe"
 CreateShortCut "$DESKTOP\Forterm Native.lnk" "$INSTDIR\forterm-native.exe"
 WriteRegStr HKCU "Software\FortermNative" "InstallDir" "$INSTDIR"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermNative" "DisplayName" "Forterm Native ${APP_VERSION}"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermNative" "DisplayVersion" "${APP_VERSION}"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermNative" "Publisher" "Forterm"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermNative" "UninstallString" '"$INSTDIR\Uninstall.exe"'
 WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermNative" "NoModify" 1
 WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermNative" "NoRepair" 1
SectionEnd
Section "Uninstall"
 SetShellVarContext current
 Delete "$DESKTOP\Forterm Native.lnk"
 Delete "$SMPROGRAMS\Forterm Native\Forterm Native.lnk"
 Delete "$SMPROGRAMS\Forterm Native\卸载.lnk"
 RMDir "$SMPROGRAMS\Forterm Native"
 Delete "$INSTDIR\forterm-native.exe"
 Delete "$INSTDIR\xshell.exe"
 Delete "$INSTDIR\xftp.exe"
 Delete "$INSTDIR\forterm-diagnose.exe"
 Delete "$INSTDIR\Uninstall.exe"
  Delete "$INSTDIR\README.md"
 Delete "$INSTDIR\MIGRATION.md"
 Delete "$INSTDIR\WINDOWS-ACCEPTANCE.md"
 Delete "$INSTDIR\VALIDATION.md"
 Delete "$INSTDIR\RELEASE-NOTES.md"
 Delete "$INSTDIR\BUILD-INFO.txt"
 Delete "$INSTDIR\COMPILED-PACKAGES.txt"
 Delete "$INSTDIR\VERIFICATION.json"
 RMDir /r "$INSTDIR\licenses"
 RMDir /r "$INSTDIR\evidence"
 RMDir "$INSTDIR"
 DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermNative"
 DeleteRegKey HKCU "Software\FortermNative"
 ; Native user configuration and OS credentials are deliberately preserved.
 ; Electron Forterm and its configuration/registry are never touched.
SectionEnd
