@echo off
REM Deploy RevitCortex R25 build to BOTH user-scope and machine-scope addin folders.
REM Run AFTER closing Revit 2025 (to release file locks).

set SRC_TOOLS=C:\Users\luigi.dattilo\Desktop\ClaudeCode\RevitCortex\src\RevitCortex.Tools\bin\Debug R25\net8.0-windows10.0.19041.0
set SRC_PLUGIN=C:\Users\luigi.dattilo\Desktop\ClaudeCode\RevitCortex\src\RevitCortex.Plugin\bin\Debug R25\net8.0-windows10.0.19041.0
set DST_USER=C:\Users\luigi.dattilo\AppData\Roaming\Autodesk\Revit\Addins\2025\RevitCortex
set DST_MACHINE=C:\ProgramData\Autodesk\Revit\Addins\2025\RevitCortex

echo Checking for running Revit processes...
tasklist /FI "IMAGENAME eq Revit.exe" 2>NUL | find /I "Revit.exe" >NUL
if %ERRORLEVEL% EQU 0 (
    echo ERROR: Revit.exe is still running. Close Revit 2025 first.
    pause
    exit /b 1
)

echo.
echo --- Deploying to USER scope: %DST_USER%
copy /Y "%SRC_TOOLS%\RevitCortex.Tools.dll" "%DST_USER%\" >NUL || goto :err
copy /Y "%SRC_TOOLS%\RevitCortex.Tools.pdb" "%DST_USER%\" >NUL || goto :err
copy /Y "%SRC_PLUGIN%\RevitCortex.Plugin.dll" "%DST_USER%\" >NUL || goto :err
copy /Y "%SRC_PLUGIN%\RevitCortex.Plugin.pdb" "%DST_USER%\" >NUL || goto :err
copy /Y "%SRC_PLUGIN%\RevitCortex.Core.dll" "%DST_USER%\" >NUL || goto :err
copy /Y "%SRC_PLUGIN%\RevitCortex.Core.pdb" "%DST_USER%\" >NUL || goto :err
echo USER scope deploy OK.

echo.
echo --- Deploying to MACHINE scope: %DST_MACHINE%
echo (requires admin elevation; UAC prompt expected)
powershell -NoProfile -Command "Start-Process cmd -ArgumentList '/c copy /Y \"%SRC_TOOLS%\RevitCortex.Tools.dll\" \"%DST_MACHINE%\\\" ^&^& copy /Y \"%SRC_TOOLS%\RevitCortex.Tools.pdb\" \"%DST_MACHINE%\\\" ^&^& copy /Y \"%SRC_PLUGIN%\RevitCortex.Plugin.dll\" \"%DST_MACHINE%\\\" ^&^& copy /Y \"%SRC_PLUGIN%\RevitCortex.Plugin.pdb\" \"%DST_MACHINE%\\\" ^&^& copy /Y \"%SRC_PLUGIN%\RevitCortex.Core.dll\" \"%DST_MACHINE%\\\" ^&^& copy /Y \"%SRC_PLUGIN%\RevitCortex.Core.pdb\" \"%DST_MACHINE%\\\"' -Verb RunAs -Wait"
echo MACHINE scope deploy attempted.

echo.
echo Verify timestamps:
dir "%DST_USER%\RevitCortex.Tools.dll" | find ".dll"
dir "%DST_MACHINE%\RevitCortex.Tools.dll" | find ".dll"
echo.
echo Done. You can now restart Revit 2025.
pause
exit /b 0

:err
echo ERROR: deploy failed.
pause
exit /b 1
