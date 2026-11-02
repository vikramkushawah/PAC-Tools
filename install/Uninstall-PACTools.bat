@echo off
rem PAC Tools - remove the Revit add-in for the current user.
rem Your projects are untouched: this removes the add-in, not any data.

setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall %*
set EXITCODE=%ERRORLEVEL%
echo.
pause
exit /b %EXITCODE%
