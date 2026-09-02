@echo off
rem ── OpenUnity.bat ────────────────────────────────────────────────────
rem Double-click to open the GameDevelop project in Unity.
rem  • Works from wherever the repo lives (path is relative to this file).
rem  • Prefers our pinned editor (6000.5.10f1), falls back to whatever
rem    Unity Hub has installed if that exact version is missing.
rem  • Writes the editor log to Logs\editor.log (gitignored) so tooling
rem    can watch compiles.
rem  • Warns if the project already looks open (UnityLockfile) — press a
rem    key to launch anyway (covers a stale lock after a crash).
setlocal

set "PROJ=%~dp0"
set "PROJ=%PROJ:~0,-1%"

set "UNITY=C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe"
if not exist "%UNITY%" (
    for /d %%D in ("C:\Program Files\Unity\Hub\Editor\*") do (
        if exist "%%D\Editor\Unity.exe" set "UNITY=%%D\Editor\Unity.exe"
    )
)
if not exist "%UNITY%" (
    echo No Unity editor found under C:\Program Files\Unity\Hub\Editor.
    echo Install 6000.5.10f1 via Unity Hub, then run this again.
    pause
    exit /b 1
)

if exist "%PROJ%\Temp\UnityLockfile" (
    echo The project looks like it is already open in Unity.
    echo If Unity is NOT running ^(crashed last time^), press any key to
    echo launch anyway — otherwise close this window.
    pause >nul
)

if not exist "%PROJ%\Logs" mkdir "%PROJ%\Logs"
start "" "%UNITY%" -projectPath "%PROJ%" -logFile "%PROJ%\Logs\editor.log"
endlocal
