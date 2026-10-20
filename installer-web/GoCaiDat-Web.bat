@echo off
rem Go cai dat EEMC Work Manager ban WEB (tu xin quyen quan tri)
chcp 65001 >nul
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '%*' -Verb RunAs"
  exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0GoCaiDat-Web.ps1" %*
