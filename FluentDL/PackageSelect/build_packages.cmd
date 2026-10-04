@echo off
rem Builds FluentDL's release packages. Run with no options, or double-click, to answer prompts.
rem Options are passed to BuildPackages.cs; run "build_packages.cmd --help" to list them.
dotnet run --file "%~dp0BuildPackages.cs" -- %*
set "exitcode=%ERRORLEVEL%"
if "%~1"=="" pause
exit /b %exitcode%
