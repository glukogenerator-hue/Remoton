@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

rem Офлайн-восстановление/установка агента Remoton.
rem Рядом должны лежать: Install-Remotely.ps1 и Remotely-Win-x64.zip (или -x86).

set SERVER=https://help.urldot.ru
set ORG=ea805c53-6147-4093-a9a6-7ab598cc61ce

if /i "%PROCESSOR_ARCHITECTURE%"=="x86" (
    set PKG=Remotely-Win-x86.zip
) else (
    set PKG=Remotely-Win-x64.zip
)

echo === Remoton agent repair ===
echo Package: %PKG%
echo Server : %SERVER%
echo.

if not exist "%~dp0Install-Remotely.ps1" (
    echo [ERROR] Install-Remotely.ps1 not found next to this file.
    pause
    exit /b 1
)
if not exist "%~dp0%PKG%" (
    echo [ERROR] %PKG% not found next to this file.
    pause
    exit /b 1
)

net session >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Run this file as Administrator.
    pause
    exit /b 1
)

echo Current state:
if exist "%ProgramFiles%\Remotely\Remotely_Agent.deps.json" (
    for %%F in ("%ProgramFiles%\Remotely\Remotely_Agent.deps.json") do echo   deps.json size: %%~zF bytes
) else (
    echo   agent files not found
)
sc query Remotely_Service | findstr /i "STATE" || echo   service not registered
echo.

echo Installing (this can take a few minutes)...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Remotely.ps1" -Path "%~dp0%PKG%" -ServerUrl %SERVER% -OrganizationId %ORG%
if errorlevel 1 (
    echo.
    echo [ERROR] Installer returned an error. See %TEMP%\Remotely_Install.txt
    pause
    exit /b 1
)

echo.
echo Result:
sc query Remotely_Service | findstr /i "STATE"
powershell -NoProfile -Command "(Get-Item '%ProgramFiles%\Remotely\Remotely_Agent.dll').VersionInfo.FileVersion" 2>nul
echo.
echo Install log: %TEMP%\Remotely_Install.txt
pause
