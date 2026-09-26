@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist tmp\tests mkdir tmp\tests
"%CSC%" /nologo /target:exe /out:tmp\tests\ChatWorkflowProbe.exe /r:System.dll /r:System.Core.dll /r:System.Xaml.dll /r:System.Web.Extensions.dll /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll" tests\ChatWorkflowProbe.cs
if errorlevel 1 exit /b 1
tmp\tests\ChatWorkflowProbe.exe "%~1" %2
exit /b %ERRORLEVEL%
