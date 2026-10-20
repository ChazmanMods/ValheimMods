@echo off
cd /d "%~dp0"
where py >nul 2>nul
if not errorlevel 1 (
    py -3 wizard.py
) else (
    python wizard.py
)
if errorlevel 1 pause
