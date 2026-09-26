Unicode True
Name "Psycho Launcher"
OutFile "PsychoLauncher_Setup.exe"

RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\PsychoLauncher"

Page directory
Page instfiles

Section "Install"
    ; =========================================================
    ; ★追加：新しいデータを入れる前に、古いgamesフォルダを完全に削除（リセット）する！
    RMDir /r "$INSTDIR\games"
    ; =========================================================

    SetOutPath "$INSTDIR"
    File /r "bin/Release/net8.0/win-x64/publish/*"

    CreateShortcut "$DESKTOP\Psycho Launcher.lnk" "$INSTDIR\PsychoLauncher.exe"
    ExecWait 'powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "Get-ChildItem -Path $\'$INSTDIR$\' -Recurse | Unblock-File"'
SectionEnd