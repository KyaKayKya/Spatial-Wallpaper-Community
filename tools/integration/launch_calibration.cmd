@echo off
cd /d "%~dp0..\.."
python tools\integration\run_calibration.py %*
if errorlevel 1 pause
