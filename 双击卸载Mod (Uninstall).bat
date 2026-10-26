@echo off
chcp 65001 >nul
title 《再再启程 One Way Tickets》键盘辅助 Mod 卸载程序
echo ============================================================
echo   《再再启程 One Way Tickets》全键盘与减负 Mod - 一键卸载
echo ============================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-Mod.ps1"
echo.
if %ERRORLEVEL% EQU 0 (
    echo [成功] 已还原为原版游戏文件。
) else (
    echo [错误] 卸载失败，请确认游戏已关闭后再试。
)
echo.
pause
