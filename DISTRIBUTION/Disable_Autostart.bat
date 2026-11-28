@echo off
chcp 65001 >nul
title PC Watch - Отключение автозапуска
echo ================================================================
echo   PC Watch: Удаление из автозапуска Windows
echo ================================================================
echo.
echo Удаление задачи из Планировщика Windows...
schtasks /delete /tn "RenderWatch" /f >nul 2>&1
echo.
echo Удаление записи из реестра автозапуска...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "RenderWatch" /f >nul 2>&1
echo.
echo [OK] PC Watch успешно удален из автозапуска.
echo.
timeout /t 3 >nul