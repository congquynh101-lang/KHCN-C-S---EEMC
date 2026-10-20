@echo off
rem Go cai dat EEMC KHCN Work Manager
chcp 65001 >nul
copy /y "%~dp0GoCaiDat.ps1" "%TEMP%\EEMC_GoCaiDat.ps1" >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%TEMP%\EEMC_GoCaiDat.ps1" %*
