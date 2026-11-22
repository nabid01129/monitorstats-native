Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
Name "MonitorStats Native"
OutFile "${BUILD}\MonitorStats-Native-Setup-1.0.24-${ARCH}.exe"
InstallDir "$LOCALAPPDATA\Programs\MonitorStats Native"
RequestExecutionLevel user
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
Function .onInit
!if "${ARCH}" == "x64"
  ${IfNot} ${RunningX64}
    MessageBox MB_OK "This edition requires 64-bit Windows."
    Abort
  ${EndIf}
!endif
FunctionEnd
Section
  SetOutPath "$INSTDIR"
  File "${BUILD}\MonitorStats Native.exe"
  File "${BUILD}\MonitorStats Native.exe.config"
  File "${PROJECT}\README.md"
  File "${PROJECT}\LICENSE"
  CreateShortcut "$DESKTOP\MonitorStats Native.lnk" "$INSTDIR\MonitorStats Native.exe"
  CreateShortcut "$SMPROGRAMS\MonitorStats Native.lnk" "$INSTDIR\MonitorStats Native.exe"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MonitorStatsNative" "DisplayName" "MonitorStats Native"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MonitorStatsNative" "DisplayVersion" "1.0.24"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MonitorStatsNative" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MonitorStatsNative"
  StrCmp $0 "" +2
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MonitorStatsNative" '"$INSTDIR\MonitorStats Native.exe"'
SectionEnd
Section "Uninstall"
  Delete "$INSTDIR\MonitorStats Native.exe"
  Delete "$INSTDIR\MonitorStats Native.exe.config"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\LICENSE"
  Delete "$DESKTOP\MonitorStats Native.lnk"
  Delete "$SMPROGRAMS\MonitorStats Native.lnk"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MonitorStatsNative"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MonitorStatsNative"
SectionEnd
