@echo off
setlocal
set "OSU_EXTERNAL_UPDATE_PROVIDER=sloptimal-portable"
start "" /D "%~dp0" "%~dp0osu!.exe" %*
endlocal
