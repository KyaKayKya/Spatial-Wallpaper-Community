@echo off
cd /d "%~dp0..\.."
python tools\library\run.py %*
if errorlevel 1 pause
