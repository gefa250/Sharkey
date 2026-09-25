@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "OUT=tmp\tests"
set "APP=bin\Release\Sharkey.exe"
if not "%~1"=="" set "APP=%~1"
if not exist "%OUT%" mkdir "%OUT%"

"%CSC%" /nologo /target:exe /out:"%OUT%\FeatureProbe.exe" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
  /r:System.Net.Http.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  tests\FeatureProbe.cs
if errorlevel 1 exit /b 1
"%OUT%\FeatureProbe.exe" "%APP%"
if errorlevel 1 exit /b 1

"%CSC%" /nologo /target:exe /out:"%OUT%\PopupInteractionProbe.exe" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
  /r:System.Xaml.dll ^
  /r:System.Data.dll ^
  /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" ^
  /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" ^
  /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll" ^
  tests\PopupInteractionProbe.cs
if errorlevel 1 exit /b 1
"%OUT%\PopupInteractionProbe.exe" "%APP%"
if errorlevel 1 exit /b 1

echo Sharkey tests passed.
endlocal
