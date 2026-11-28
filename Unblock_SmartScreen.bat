@echo off
chcp 65001 >nul
set "WATCH_EXE=%~dp0PCWatch.exe"
if not exist "%WATCH_EXE%" set "WATCH_EXE=%~dp0RenderWatch.exe"
title PC Watch - Разблокировка Windows SmartScreen
echo ================================================================
echo   Снятие метки блокировки Windows SmartScreen (Mark-of-the-Web)
echo ================================================================
echo.
echo Разблокировка файлов программы...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%~dp0' -Recurse | Unblock-File"
echo [OK] Файлы успешно разблокированы!
echo.
echo Запуск PC Watch...
start "" "%WATCH_EXE%"