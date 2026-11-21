@echo off
setlocal
cd /d "%~dp0"
set "NOTES_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%NOTES_CSC%" set "NOTES_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%NOTES_CSC%" (
  echo .NET Framework C# compiler was not found.
  exit /b 1
)
"%NOTES_CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /win32manifest:app.manifest /win32icon:assets\app.ico /out:RetroNotes95.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Runtime.Serialization.dll /r:System.Core.dll src\Program.cs src\Storage.cs src\Appearance.cs src\NoteWindow.cs
if errorlevel 1 exit /b 1
echo Built RetroNotes95.exe
endlocal
