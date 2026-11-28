@echo off
chcp 65001 >nul
title PC Watch - Завершение работы
echo ================================================================
echo   PC Watch: Полное завершение работы и освобождение файлов
echo ================================================================
echo.
echo Завершение процессов PC Watch...
taskkill /F /IM PCWatch.exe /IM RenderWatch.exe 2>nul
echo [OK] Все процессы PC Watch завершены. Папка полностью разблокирована!
timeout /t 2 >nul