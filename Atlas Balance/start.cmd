@echo off
rem Alias en ingles del arranque canonico: delega en scripts\start.ps1, que
rem lanza scripts\Launch-AtlasBalance.ps1 con C:\AtlasBalance por defecto.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start.ps1" %*
exit /b %ERRORLEVEL%
