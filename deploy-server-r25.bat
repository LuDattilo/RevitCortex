@echo off
REM Deploy RevitCortex.Server R25 build to user-local install dir.
REM Run AFTER closing Claude Code (to release file locks on Server.exe).

set SRC=C:\Users\luigi.dattilo\Desktop\ClaudeCode\RevitCortex\src\RevitCortex.Server\bin\Debug R25\net8.0\win-x64
set DST=C:\Users\luigi.dattilo\.revitcortex\server

echo Source: %SRC%
echo Destination: %DST%
echo.

REM Verify no Server.exe is running
tasklist /FI "IMAGENAME eq RevitCortex.Server.exe" 2>NUL | find /I "RevitCortex.Server.exe" >NUL
if %ERRORLEVEL% EQU 0 (
    echo ERROR: RevitCortex.Server.exe is still running.
    echo Close Claude Code first, then re-run this script.
    pause
    exit /b 1
)

echo Copying files...
xcopy /Y /E /I "%SRC%\*" "%DST%\" >NUL
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: copy failed with code %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Deploy complete. New Server.exe timestamp:
dir "%DST%\RevitCortex.Server.exe" | find ".exe"
echo.
echo Now restart Claude Code.
pause
