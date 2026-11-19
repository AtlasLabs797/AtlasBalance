@echo off
rem Lanzador canonico de arranque. Delega en la implementacion
rem scripts\Launch-AtlasBalance.ps1. El alias en ingles es start.cmd.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Launch-AtlasBalance.ps1" %*
exit /b %ERRORLEVEL%
