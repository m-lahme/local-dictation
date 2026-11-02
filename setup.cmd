@echo off
rem Runs setup.ps1 regardless of the execution policy. Started by double-click, the window stays open at the end.
setlocal EnableDelayedExpansion
set "pauseAtEnd="
set "commandLine=!cmdcmdline!"
if /i not "!commandLine:%~nx0=!"=="!commandLine!" if "%~1"=="" set "pauseAtEnd=1"
setlocal DisableDelayedExpansion
set "failed="
rem One block ending in (goto): setup.ps1 -Uninstall may delete this file, and cmd must not read it again afterwards.
rem Full paths, because cmd looks for programs in the current folder (e.g. Downloads) before PATH.
(
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" %*
if errorlevel 1 set "failed=1"
if defined pauseAtEnd pause
if defined failed ((goto) 2>nul & "%ComSpec%" /d /c exit 1) else ((goto) 2>nul & "%ComSpec%" /d /c exit 0)
)
