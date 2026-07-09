@echo off
rem Build TypeBeep.exe + TypeBeep-Setup.exe using the C# compiler bundled with Windows (.NET Framework 4)
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
taskkill /im TypeBeep.exe /f >nul 2>&1

%CSC% /nologo /codepage:65001 /target:winexe /win32icon:app.ico /out:TypeBeep.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll Program.cs
if errorlevel 1 (echo BUILD FAILED: TypeBeep.exe & exit /b 1)

%CSC% /nologo /codepage:65001 /target:winexe /win32icon:app.ico /out:TypeBeep-Setup.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /resource:TypeBeep.exe,TypeBeep.exe Setup.cs
if errorlevel 1 (echo BUILD FAILED: TypeBeep-Setup.exe & exit /b 1)

echo BUILD OK - TypeBeep.exe + TypeBeep-Setup.exe
