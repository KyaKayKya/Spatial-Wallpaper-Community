@echo off
setlocal
set "SPATIAL_CURRENT=%~dp0..\..\Prototype\Builds\Desktop-SDK-Quality-20261111-r5"
if not exist "%SPATIAL_CURRENT%\Launch.exe" (
  echo Local integrated build is missing. See README.md for local build setup.
  exit /b 1
)
start "" /d "%SPATIAL_CURRENT%" "%SPATIAL_CURRENT%\Launch.exe" %*
exit /b
