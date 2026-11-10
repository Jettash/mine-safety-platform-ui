@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\set-mysql-connection.ps1" %*
if errorlevel 1 pause
