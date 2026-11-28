@echo off
chcp 65001 >nul
set "WATCH_EXE=%~dp0PCWatch.exe"
if not exist "%WATCH_EXE%" set "WATCH_EXE=%~dp0RenderWatch.exe"
title PC Watch - Включение автозапуска
echo ================================================================
echo   PC Watch: Добавление в автозапуск Windows
echo ================================================================
echo.
echo Регистрация задачи в Планировщике Windows с наивысшими правами...
schtasks /create /tn "RenderWatch" /tr "\"%WATCH_EXE%\" --taskbar" /sc onlogon /rl highest /f
if errorlevel 1 exit /b 1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "RenderWatch" /f >nul 2>&1
echo.
echo [OK] PC Watch успешно добавлен в автозапуск!
echo      Приложение будет запускаться на панель задач при входе в систему.
echo.
timeout /t 3 >nul
