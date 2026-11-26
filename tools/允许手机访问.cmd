@echo off
setlocal
set "PORT=8737"
set "RULE=AutoApply 手机页面"

rem 需要管理员：加/改防火墙入站规则。右键本文件 → 以管理员身份运行。
net session >nul 2>&1
if errorlevel 1 (
  echo 需要管理员权限。请右键这个文件，选择「以管理员身份运行」。
  pause
  exit /b 1
)

echo 放行 TCP %PORT%（仅限本局域网 %RULE%）...
netsh advfirewall firewall delete rule name="%RULE%" >nul 2>&1
netsh advfirewall firewall add rule name="%RULE%" dir=in action=allow protocol=TCP localport=%PORT% remoteip=localsubnet profile=any >nul
if errorlevel 1 (
  echo 失败：没能加上防火墙规则。
  pause
  exit /b 1
)

echo.
echo 已经放行。手机连同一个 Wi-Fi，浏览器打开「平台健康」页底部显示的地址即可。
echo 只允许本局域网访问；不要把 %PORT% 端口映射到公网。
echo.
pause
