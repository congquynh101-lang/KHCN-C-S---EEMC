@echo off
rem Cai dat EEMC KHCN Work Manager (khong can quyen quan tri)
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0CaiDat.ps1" %*
