@echo off
title RevitCortex Installer
echo.
echo ========================================
echo    RevitCortex Installer
echo ========================================
echo.
echo Avvio installazione...
echo.

powershell -ExecutionPolicy Bypass -File "%~dp0install.ps1"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERRORE] Installazione fallita.
    echo Prova ad eseguire come Amministratore: tasto destro ^> Esegui come amministratore
    echo.
)

pause
