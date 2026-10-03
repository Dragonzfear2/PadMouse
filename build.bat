@echo off
rem Builds dist\PadMouse.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No Visual Studio or SDK needed.
rem Optional code signing: set SIGN_PFX=path\to\cert.pfx and SIGN_PASSWORD=... before running.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (echo Could not find csc.exe from .NET Framework 4.x & exit /b 1)
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 /out:dist\PadMouse.exe /win32icon:src\app.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 (echo Build failed & exit /b 1)
echo Built dist\PadMouse.exe
if "%SIGN_PFX%"=="" goto :eof
set SIGNTOOL=
for /f "delims=" %%i in ('dir /b /s "%ProgramFiles(x86)%\Windows Kits\10\bin\*signtool.exe" 2^>nul ^| findstr /i "x64"') do set SIGNTOOL=%%i
if "%SIGNTOOL%"=="" (echo signtool.exe not found - install the Windows SDK & exit /b 1)
"%SIGNTOOL%" sign /f "%SIGN_PFX%" /p "%SIGN_PASSWORD%" /fd sha256 /tr http://timestamp.digicert.com /td sha256 /d "PadMouse" dist\PadMouse.exe
if errorlevel 1 (echo Signing failed & exit /b 1)
echo Signed dist\PadMouse.exe
