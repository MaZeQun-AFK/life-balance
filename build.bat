@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo   [x] 没有找到 dotnet 命令。
  echo       请先安装 .NET SDK 6.0 或更高版本：https://dotnet.microsoft.com/download
  echo.
  pause
  exit /b 1
)

rem 只装了运行时、没装 SDK 的情况：dotnet 存在但 publish 不可用
dotnet --list-sdks >nul 2>nul
if errorlevel 1 (
  echo.
  echo   [x] 检测到 dotnet，但没有安装 .NET SDK。
  echo       你装的可能只是运行时。请安装 SDK：
  echo       https://dotnet.microsoft.com/download
  echo.
  pause
  exit /b 1
)

rem 先清掉旧产物，避免上次的残留文件被带进去
echo 正在清理旧产物 ...
if exist "人生余额" rd /S /Q "人生余额"

echo 正在编译到  人生余额\ ...
dotnet publish "src\LifeBalance.csproj" -c Release -r win-x64 --self-contained false -o "人生余额" --nologo
if errorlevel 1 goto failed

echo 复制界面文件 ...
if not exist "人生余额\www" mkdir "人生余额\www"
copy /Y "src\www\index.html" "人生余额\www\" >nul

echo.
echo   完成。双击运行： 人生余额\人生余额.exe
echo   注意：www 文件夹要和 exe 放在一起。
goto end

:failed
echo.
echo   [x] 编译失败，请看上面的错误信息。

:end
echo.
pause
