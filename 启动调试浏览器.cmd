@echo off
chcp 65001 >nul
title 启动程序专用的调试浏览器
rem ============================================================
rem  只在「点『重开』拉不起浏览器」时才需要它。
rem  用你自己双击的方式启动 Chrome：独立配置目录 + 调试端口 9333。
rem  起来之后回到 AutoApply 点「全部检查」，端口那一格会变成「已连接」。
rem ============================================================
set "PROFILE=%~dp0data\browser-profile"
set "CHROME="
for %%P in (
  "%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"
  "%ProgramFiles%\Google\Chrome\Application\chrome.exe"
  "%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"
) do if exist %%P set "CHROME=%%~P"

if not defined CHROME (
  echo 没找到 chrome.exe，请把下面这行里的路径改成你的浏览器：
  echo   chrome.exe --user-data-dir="%PROFILE%" --remote-debugging-port=9333
  pause
  exit /b 1
)

echo 正在启动：%CHROME%
echo 配置目录：%PROFILE%
start "" "%CHROME%" --user-data-dir="%PROFILE%" --remote-debugging-port=9333 --no-first-run --no-default-browser-check --window-size=1280,900
echo.
echo 已发出启动命令。窗口出来后：
echo   1) 在这个窗口里登录四个平台（第一次必须登录，之后会记住）
echo   2) 回到 AutoApply 点「全部检查」
timeout /t 6 >nul
