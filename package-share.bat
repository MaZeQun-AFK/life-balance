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

rem 先清掉旧产物，避免上次的残留文件（尤其是个人的 migrate.js）被带进去
echo 正在清理旧产物 ...
if exist "分享版" rd /S /Q "分享版"

echo 正在打包分享版（自带 .NET 运行时，对方不用安装任何东西）...
dotnet publish "src\LifeBalance.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "分享版" --nologo
if errorlevel 1 goto failed

del /Q "分享版\*.xml" 2>nul
if not exist "分享版\www" mkdir "分享版\www"
copy /Y "src\www\index.html" "分享版\www\" >nul

echo.
echo   完成。把 分享版 文件夹压缩成 zip 发给别人。
echo   对方解压后双击 人生余额.exe 即可（www 文件夹要一起带上）。
goto end

:failed
echo.
echo   [x] 打包失败。注意：打包前请完全退出正在运行的 人生余额。

:end
echo.
pause
