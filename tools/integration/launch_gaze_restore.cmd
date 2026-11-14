@echo off
cd /d "%~dp0..\.."
python tools\integration\run_gaze_preview.py --restore
if errorlevel 1 pause
