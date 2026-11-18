@echo off
chcp 65001 >nul
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK not found. Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

tasklist /FI "IMAGENAME eq EdgeMark.exe" 2>nul | find /I "EdgeMark.exe" >nul
if not errorlevel 1 goto running

echo Building a self-contained EdgeMark.exe ^(first build takes longer^)...
dotnet publish EdgeMark.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none ^
  -o dist
if errorlevel 1 (
  echo.
  echo Build failed - see the errors above.
  pause
  exit /b 1
)

copy /Y "dist\EdgeMark.exe" "EdgeMark.exe" >nul
if errorlevel 1 goto running
echo.
echo Done: EdgeMark.exe is next to this file. It runs without .NET installed.
explorer.exe /select,"%~dp0EdgeMark.exe"
pause
exit /b 0

:running
echo.
echo EdgeMark is running and the exe is locked. Exit it from the tray icon menu, then run the build again.
pause
exit /b 1
