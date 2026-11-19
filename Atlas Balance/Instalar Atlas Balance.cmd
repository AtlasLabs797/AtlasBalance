@echo off
rem Lanzador canonico de instalacion. Delega en scripts\install.ps1, que
rem autoeleva y llama a la implementacion scripts\Instalar-AtlasBalance.ps1.
rem El alias en ingles es install.cmd.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1" %*
exit /b %ERRORLEVEL%
