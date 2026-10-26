@echo off
chcp 65001 >nul
title 《再再启程 One Way Tickets》键盘辅助 Mod 安装程序
echo ============================================================
echo   《再再启程 One Way Tickets》全键盘与减负 Mod - 一键安装
echo ============================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Mod.ps1" -SkipBuild
echo.
if %ERRORLEVEL% EQU 0 (
    echo [成功] 安装完成！现在可以直接启动游戏体验了。
) else (
    echo [错误] 安装失败，请确认游戏已关闭后再试。
)
echo.
pause
