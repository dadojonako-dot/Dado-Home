@echo off
if not exist "%~dp0runtime\Access.txt" (
  echo Run the Start launcher first.
  pause
  exit /b 1
)
notepad.exe "%~dp0runtime\Access.txt"

