; ==============================================================================
; iMac Fan Control - NSIS Windows Installer Script
; ==============================================================================

Target x86-ansi

!include "MUI2.nsh"
!include "x64.nsh"

; --- General Attributes ---
Name "iMac Fan Control"
OutFile "../publish/iMacFanControl_Setup.exe"
InstallDir "$PROGRAMFILES64\iMacFanControl"
InstallDirRegKey HKLM "Software\iMacFanControl" "InstallDir"
RequestExecutionLevel admin
SetCompressor /SOLID lzma

; --- Interface Settings ---
!define MUI_ABORTWARNING
!define MUI_ICON "${NSISDIR}\Contrib\Graphics\Icons\modern-install.ico"
!define MUI_UNICON "${NSISDIR}\Contrib\Graphics\Icons\modern-uninstall.ico"

; --- Header / Welcome Page custom texts ---
!define MUI_WELCOMEPAGE_TITLE "Instalador de iMac Fan Control"
!define MUI_WELCOMEPAGE_TEXT "Bienvenido al asistente de instalacion de iMac Fan Control para Windows (Boot Camp).$\r$\n$\r$\nEsta herramienta permite monitorizar temperaturas y controlar las RPM de los ventiladores de tu iMac de forma segura.$\r$\n$\r$\nHaz clic en Siguiente para continuar."

; --- Pages ---
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES

; Finish Page options
!define MUI_FINISHPAGE_RUN "$INSTDIR\iMacFanControl.Diagnostic.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Ejecutar iMac Fan Control ahora"

!insertmacro MUI_PAGE_FINISH

; Uninstaller Pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

; --- Language ---
!insertmacro MUI_LANGUAGE "English"

; --- Functions ---
Function .onInit
    ${IfNot} ${RunningX64}
        MessageBox MB_ICONSTOP|MB_OK "Este instalador requiere una version de Windows de 64 bits."
        Abort
    ${EndIf}
FunctionEnd

; --- Installation Section ---
Section "iMac Fan Control" SecCore
    SectionIn RO

    ; Set 64-bit installation directory
    ${DisableX64FSRedirection}
    SetOutPath "$INSTDIR"

    ; Copy all standalone publish files
    File /r "..\publish\windows-x64-standalone\*.*"

    ; Store installation path
    WriteRegStr HKLM "Software\iMacFanControl" "InstallDir" "$INSTDIR"

    ; Create Uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    ; Windows Add/Remove Programs integration
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\iMacFanControl" "DisplayName" "iMac Fan Control"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\iMacFanControl" "DisplayIcon" "$INSTDIR\iMacFanControl.Diagnostic.exe,0"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\iMacFanControl" "DisplayVersion" "1.0.0"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\iMacFanControl" "Publisher" "iMac Fan Control"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\iMacFanControl" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\iMacFanControl" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\iMacFanControl" "InstallLocation" "$INSTDIR"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\iMacFanControl" "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\iMacFanControl" "NoRepair" 1

    ; Create Start Menu Shortcuts
    CreateDirectory "$SMPROGRAMS\iMac Fan Control"
    CreateShortcut "$SMPROGRAMS\iMac Fan Control\iMac Fan Control.lnk" "$INSTDIR\iMacFanControl.Diagnostic.exe" "" "$INSTDIR\iMacFanControl.Diagnostic.exe" 0
    CreateShortcut "$SMPROGRAMS\iMac Fan Control\iMac Fan Control (Bandeja Tray).lnk" "$INSTDIR\iMacFanControl.Diagnostic.exe" "--tray" "$INSTDIR\iMacFanControl.Diagnostic.exe" 0
    CreateShortcut "$SMPROGRAMS\iMac Fan Control\iMac Fan Control (Monitor en Vivo).lnk" "$INSTDIR\iMacFanControl.Diagnostic.exe" "--watch" "$INSTDIR\iMacFanControl.Diagnostic.exe" 0
    CreateShortcut "$SMPROGRAMS\iMac Fan Control\Desinstalar iMac Fan Control.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0

    ; Create Desktop Shortcut
    CreateShortcut "$DESKTOP\iMac Fan Control.lnk" "$INSTDIR\iMacFanControl.Diagnostic.exe" "" "$INSTDIR\iMacFanControl.Diagnostic.exe" 0
SectionEnd

; --- Uninstaller Section ---
Section "Uninstall"
    ${DisableX64FSRedirection}

    ; Stop autostart task if registered
    ExecWait '"$INSTDIR\iMacFanControl.Diagnostic.exe" --uninstall-autostart'

    ; Remove files and directory
    RMDir /r "$INSTDIR"

    ; Remove Shortcuts
    Delete "$DESKTOP\iMac Fan Control.lnk"
    RMDir /r "$SMPROGRAMS\iMac Fan Control"

    ; Remove Registry keys
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\iMacFanControl"
    DeleteRegKey HKLM "Software\iMacFanControl"
SectionEnd
