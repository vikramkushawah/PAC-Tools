@echo off
rem PAC Tools - build and install the Revit add-in for the current user.
rem Double-click this file. No administrator rights are needed.
rem
rem For another Revit version, run from a terminal instead:
rem     powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1 -RevitVersion 2026

setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
set EXITCODE=%ERRORLEVEL%
echo.
pause
exit /b %EXITCODE%
