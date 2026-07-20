@echo off
setlocal

set "APP_NAME=BV6Tools"
set "RUN_KEY=HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"

echo Removing startup entries for %APP_NAME%...
echo.

reg delete "%RUN_KEY%" /v "%APP_NAME%" /f >nul 2>&1

schtasks /Query /TN "%APP_NAME%" >nul 2>&1
if %errorlevel%==0 (
    schtasks /Delete /TN "%APP_NAME%" /F
    if errorlevel 1 (
        echo.
        echo Could not remove the scheduled task.
        echo Please Run this .bat as Admin, then try again.
        echo.
        pause
        exit /b 1
    )
)

echo.
echo Cleanup complete. You can now delete this folder.
pause