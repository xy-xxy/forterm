; Forterm Studio, for the current user only. Built by tooling/package-studio-windows.py.
; It installs into one folder whatever the version, so that the paths a controller has been given stay true. It replaces
; and removes only files it has put there: a folder with files of somebody else's that have the same names is refused.
Unicode true
ManifestDPIAware true
!include "MUI2.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "LogicLib.nsh"
!include "nsDialogs.nsh"
!include "WinMessages.nsh"
!include "FileFunc.nsh"
!ifndef APP_VERSION | APP_NUMERIC | SOURCE_DIR | OUTPUT_FILE | UNINSTALL_FILES | TAKEN_FILES | ICON_FILE | ESTIMATED_KB
!error "APP_VERSION, APP_NUMERIC, SOURCE_DIR, OUTPUT_FILE, UNINSTALL_FILES, TAKEN_FILES, ICON_FILE and ESTIMATED_KB are required"
!endif
!define PRODUCT "Forterm Studio"
!define REG_UNINSTALL "Software\Microsoft\Windows\CurrentVersion\Uninstall\FortermStudio"
!define REG_PRODUCT "Software\Forterm\Studio"
!define UNINSTALLER "Uninstall-Forterm-Studio.exe"

Name "${PRODUCT}"
BrandingText "${PRODUCT} ${APP_VERSION}"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\${PRODUCT}"
InstallDirRegKey HKCU "${REG_PRODUCT}" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
; A file that cannot be written ends the installation (silently: with 2) instead of being left out.
AllowSkipFiles off
ShowInstDetails nevershow
ShowUninstDetails nevershow

!define MUI_ICON "${ICON_FILE}"
!define MUI_UNICON "${ICON_FILE}"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "安装 ${PRODUCT} ${APP_VERSION}"
!define MUI_WELCOMEPAGE_TEXT "只为当前用户安装，不需要管理员权限。$\r$\n$\r$\n已经装过时会在原来的文件夹里升级，设置、连接和凭据都保留。$\r$\n$\r$\n请先关闭正在运行的 Forterm。"
!define MUI_FINISHPAGE_TITLE "${PRODUCT} 已安装"
!define MUI_FINISHPAGE_TEXT "控制器用的两个路径也可以在 Forterm 的“系统配置 → 控制器接入”里查看和复制。"
!define MUI_FINISHPAGE_RUN "$INSTDIR\Forterm.exe"
!define MUI_FINISHPAGE_RUN_TEXT "现在打开 Forterm"
!define MUI_FINISHPAGE_SHOWREADME ""
!define MUI_FINISHPAGE_SHOWREADME_TEXT "在桌面放一个快捷方式"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION DesktopShortcut
!define MUI_UNCONFIRMPAGE_TEXT_TOP "将删除 ${PRODUCT} 的程序文件。设置、连接和凭据会保留，这个文件夹里你自己的文件也不会动。"

!insertmacro MUI_PAGE_WELCOME
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE LeaveDirectory
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
Page custom EntriesPage
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

VIProductVersion "${APP_NUMERIC}"
VIAddVersionKey /LANG=2052 "ProductName" "${PRODUCT}"
VIAddVersionKey /LANG=2052 "FileDescription" "${PRODUCT} 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Forterm contributors"

; Exit codes of a silent run: 2 = not this Windows, or a file could not be written; 3 = the folder holds files of the same
; names that are not ours; 4 = the program is in use; 5 = a file of the version before could not be removed.
Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "需要 64 位的 Windows 10 或更高版本。" /SD IDOK
    SetErrorLevel 2
    Quit
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "需要 Windows 10 或更高版本。" /SD IDOK
    SetErrorLevel 2
    Quit
  ${EndIf}
FunctionEnd

; A program that is running cannot be opened for writing. It is never ended from here: it may hold open sessions. The
; entries count too: one that is showing a message keeps the files it shares with the program.
!macro PROBE FILE
  ${If} ${FileExists} "$INSTDIR\${FILE}"
    ClearErrors
    FileOpen $0 "$INSTDIR\${FILE}" a
    ${If} ${Errors}
      StrCpy $3 "${FILE}"
    ${Else}
      FileClose $0
    ${EndIf}
  ${EndIf}
!macroend
!macro CHECK_RUNNING UN
Function ${UN}CheckRunning
  ${Do}
    StrCpy $3 ""
    !insertmacro PROBE "forterm-diagnose.exe"
    !insertmacro PROBE "xftp.exe"
    !insertmacro PROBE "xshell.exe"
    !insertmacro PROBE "Forterm.exe"
    ${If} $3 == ""
      ${ExitDo}
    ${EndIf}
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "$3 正在使用（可能还有打开的会话，或没有关掉的提示），或者没有权限改写它。$\r$\n请先关闭 Forterm，然后点“重试”。" /SD IDCANCEL IDRETRY +3
    SetErrorLevel 4
    Quit
  ${Loop}
FunctionEnd
!macroend
!insertmacro CHECK_RUNNING ""
!insertmacro CHECK_RUNNING "un."

; $3 = a file in the folder that would be replaced although it is not ours, or nothing.
Function CheckFolder
  StrCpy $3 ""
  ReadRegStr $1 HKCU "${REG_PRODUCT}" "InstallDir"
  ${If} $1 != $INSTDIR
    !include "${TAKEN_FILES}"
  ${EndIf}
FunctionEnd

Function LeaveDirectory
  Call CheckFolder
  ${If} $3 != ""
    MessageBox MB_OK|MB_ICONEXCLAMATION "这个文件夹里已经有 $3，它不是 Forterm Studio 安装的，不会覆盖。$\r$\n请换一个文件夹。"
    Abort
  ${EndIf}
FunctionEnd

Function DesktopShortcut
  SetShellVarContext current
  CreateShortCut "$DESKTOP\${PRODUCT}.lnk" "$INSTDIR\Forterm.exe"
  WriteRegDWORD HKCU "${REG_PRODUCT}" "DesktopShortcut" 1 ; ours to remove
FunctionEnd

Function EntriesPage
  !insertmacro MUI_HEADER_TEXT "接入控制器" "把这两个位置填到控制器里"
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 28u "在控制器（SSO 或堡垒机客户端）里，把 Xshell、Xftp 的程序位置改成下面两行，参数不用改。可以选中复制。以后升级仍装在这个文件夹，不用再改。"
  Pop $0
  ${NSD_CreateLabel} 0 36u 100% 10u "Xshell（SSH 终端）"
  Pop $0
  ${NSD_CreateText} 0 48u 100% 13u "$INSTDIR\xshell.exe"
  Pop $0
  SendMessage $0 ${EM_SETREADONLY} 1 0
  ${NSD_CreateLabel} 0 70u 100% 10u "Xftp（文件传输）"
  Pop $0
  ${NSD_CreateText} 0 82u 100% 13u "$INSTDIR\xftp.exe"
  Pop $0
  SendMessage $0 ${EM_SETREADONLY} 1 0
  ${NSD_CreateLabel} 0 106u 100% 20u "不用控制器的话，直接打开 Forterm 就可以。"
  Pop $0
  nsDialogs::Show
FunctionEnd

Section "${PRODUCT}"
  SetShellVarContext current
  Call CheckFolder
  ${If} $3 != ""
    MessageBox MB_OK|MB_ICONSTOP "这个文件夹里已经有 $3，它不是 Forterm Studio 安装的，没有安装。" /SD IDOK
    SetErrorLevel 3
    Quit
  ${EndIf}
  Call CheckRunning
  ; Remembered before anything is changed: should this be interrupted, the next run comes back here and puts it right.
  ReadRegStr $1 HKCU "${REG_PRODUCT}" "InstallDir"
  WriteRegStr HKCU "${REG_PRODUCT}" "InstallDir" "$INSTDIR"
  ; The version before removes exactly its own files, so that none of them stays behind; shortcuts and the registry stay.
  ; Only an installation of ours: nobody else's program is ever run.
  ${If} $1 == $INSTDIR
  ${AndIf} ${FileExists} "$INSTDIR\${UNINSTALLER}"
    ExecWait '"$INSTDIR\${UNINSTALLER}" /S /UPGRADE _?=$INSTDIR' $0
    ${If} $0 <> 0
      MessageBox MB_OK|MB_ICONSTOP "旧版本没有删除干净（$0），没有安装新版本。请关闭 Forterm 后再运行一次。" /SD IDOK
      SetErrorLevel 5
      Quit
    ${EndIf}
  ${EndIf}
  SetOutPath "$INSTDIR"
  SetOverwrite on
  File /r "${SOURCE_DIR}\*"
  WriteUninstaller "$INSTDIR\${UNINSTALLER}"
  CreateShortCut "$SMPROGRAMS\${PRODUCT}.lnk" "$INSTDIR\Forterm.exe"
  WriteRegStr HKCU "${REG_UNINSTALL}" "DisplayName" "${PRODUCT}"
  WriteRegStr HKCU "${REG_UNINSTALL}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${REG_UNINSTALL}" "Publisher" "Forterm"
  WriteRegStr HKCU "${REG_UNINSTALL}" "DisplayIcon" "$INSTDIR\Forterm.exe"
  WriteRegStr HKCU "${REG_UNINSTALL}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${REG_UNINSTALL}" "UninstallString" '"$INSTDIR\${UNINSTALLER}"'
  WriteRegStr HKCU "${REG_UNINSTALL}" "QuietUninstallString" '"$INSTDIR\${UNINSTALLER}" /S'
  WriteRegDWORD HKCU "${REG_UNINSTALL}" "EstimatedSize" ${ESTIMATED_KB}
  WriteRegDWORD HKCU "${REG_UNINSTALL}" "NoModify" 1
  WriteRegDWORD HKCU "${REG_UNINSTALL}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  Call un.CheckRunning
  ${GetParameters} $1
  ClearErrors
  ${GetOptions} $1 "/UPGRADE" $2
  ${If} ${Errors}
    StrCpy $2 "remove"
  ${Else}
    StrCpy $2 "upgrade"
  ${EndIf}
  StrCpy $3 "" ; a file that is still there
  !include "${UNINSTALL_FILES}"
  ${If} $2 == "remove"
    Delete "$INSTDIR\${UNINSTALLER}"
    RMDir "$INSTDIR"
    ; Only what belongs to this copy: another one may have been installed since.
    ReadRegStr $1 HKCU "${REG_PRODUCT}" "InstallDir"
    ${If} $1 == $INSTDIR
      Delete "$SMPROGRAMS\${PRODUCT}.lnk"
      ReadRegDWORD $1 HKCU "${REG_PRODUCT}" "DesktopShortcut"
      ${If} $1 == 1
        Delete "$DESKTOP\${PRODUCT}.lnk"
      ${EndIf}
      DeleteRegKey HKCU "${REG_UNINSTALL}"
      DeleteRegValue HKCU "${REG_PRODUCT}" "DesktopShortcut"
      DeleteRegValue HKCU "${REG_PRODUCT}" "InstallDir"
      DeleteRegKey /ifempty HKCU "${REG_PRODUCT}"
    ${EndIf}
  ${EndIf}
  ${If} $3 != ""
    MessageBox MB_OK|MB_ICONEXCLAMATION "有文件没有删除，例如 $3。" /SD IDOK
    SetErrorLevel 5
  ${EndIf}
  ; Settings, connections and credentials are elsewhere and stay.
SectionEnd
