@echo off
setlocal

pushd "%~dp0.." || exit /b 1

echo === Build solution ===
dotnet build "JsonConfigEditor.sln" --configuration Debug
if errorlevel 1 goto failed

echo === Run tests ===
dotnet test "JsonConfigEditor.sln" --configuration Debug --no-build
if errorlevel 1 goto failed

echo === Launch JsonConfigEditor ===
dotnet run --no-build --configuration Debug --project "src\JsonConfigEditor.App\JsonConfigEditor.App.csproj" -- %*
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%

:failed
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
