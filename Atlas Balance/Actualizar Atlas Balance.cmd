@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Actualizar-AtlasBalance.ps1" %*
exit /b %ERRORLEVEL%
