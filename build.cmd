@echo off
setlocal
set "MSBUILD=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
"%MSBUILD%" GlobalTranslator.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m
endlocal
