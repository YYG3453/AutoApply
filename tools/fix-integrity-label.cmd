@echo off
chcp 65001 >nul
setlocal
title AutoApply - Fix Low Integrity Label

net session >nul 2>&1
if errorlevel 1 (
  echo Need administrator rights. A UAC prompt will appear - click Yes.
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0fix-integrity-label.ps1"

echo.
echo [4/4] Closing AutoApply processes that were started before this fix...
echo       (an old low-integrity instance would otherwise take over the next double-click)
taskkill /IM AutoApply.exe /F >nul 2>&1
echo.
echo Done. Now double-click AutoApply.exe - it will run at normal integrity.
pause
