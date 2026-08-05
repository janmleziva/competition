@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\deploy.ps1" %*

endlocal
