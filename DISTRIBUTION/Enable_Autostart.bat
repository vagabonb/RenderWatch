@echo off
chcp 65001 >nul
title RENDER WATCH - Р’РєР»СЋС‡РµРЅРёРµ Р°РІС‚РѕР·Р°РїСѓСЃРєР°
echo ================================================================
echo   RENDER WATCH: Р”РѕР±Р°РІР»РµРЅРёРµ РІ Р°РІС‚РѕР·Р°РїСѓСЃРє Windows
echo ================================================================
echo.
echo Р РµРіРёСЃС‚СЂР°С†РёСЏ Р·Р°РґР°С‡Рё РІ РџР»Р°РЅРёСЂРѕРІС‰РёРєРµ Windows СЃ РЅР°РёРІС‹СЃС€РёРјРё РїСЂР°РІР°РјРё...
schtasks /create /tn "RenderWatch" /tr "\"%~dp0RenderWatch.exe\" --taskbar" /sc onlogon /rl highest /f
echo.
echo Р”РѕР±Р°РІР»РµРЅРёРµ Р·Р°РїРёСЃРё РІ СЂРµРµСЃС‚СЂ Р°РІС‚РѕР·Р°РїСѓСЃРєР°...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "RenderWatch" /t REG_SZ /d "\"%~dp0RenderWatch.exe\" --taskbar" /f >nul 2>&1
echo.
echo [OK] RENDER WATCH СѓСЃРїРµС€РЅРѕ РґРѕР±Р°РІР»РµРЅ РІ Р°РІС‚РѕР·Р°РїСѓСЃРє!
echo      РџСЂРёР»РѕР¶РµРЅРёРµ Р±СѓРґРµС‚ Р·Р°РїСѓСЃРєР°С‚СЊСЃСЏ РЅР° РїР°РЅРµР»СЊ Р·Р°РґР°С‡ РїСЂРё РІС…РѕРґРµ РІ СЃРёСЃС‚РµРјСѓ.
echo.
timeout /t 3 >nul