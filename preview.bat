@echo off
setlocal
set "PAGE=%~dp0src\www\index.html"
if /I "%~1"=="widget" set "PAGE=%PAGE%?widget=1"
if not exist "%PAGE%" goto missing
start "" "%PAGE%"
goto :eof

:missing
echo   ’“≤ªµΩ src\www\index.html
pause
