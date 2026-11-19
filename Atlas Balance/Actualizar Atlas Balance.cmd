@echo off
rem Lanzador canonico de actualizacion. Delega en scripts\update.ps1, que
rem autoeleva y llama a la implementacion scripts\Actualizar-AtlasBalance.ps1.
rem El alias en ingles es update.cmd.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\update.ps1" %*
exit /b %ERRORLEVEL%
