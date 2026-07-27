@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "Builds\Windows" mkdir "Builds\Windows"
"%CSC%" /nologo /target:winexe /out:"Builds\Windows\MenuBarTetraTray.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "WindowsTrayLauncher\Program.cs"
if errorlevel 1 exit /b %errorlevel%
echo Built Builds\Windows\MenuBarTetraTray.exe
