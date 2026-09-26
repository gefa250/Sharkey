@echo off
setlocal
set "MSBUILD=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
set "OUT=bin\Release"
if not "%~1"=="" set "OUT=%~1"
if "%OUT:~-1%"=="\" set "OUT=%OUT:~0,-1%"
"%MSBUILD%" GlobalTranslator.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /p:OutputPath="%OUT%" /m
exit /b %ERRORLEVEL%
