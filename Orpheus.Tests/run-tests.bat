@echo off
REM Orpheus Test Runner Script for Windows
REM This script runs all tests for the Orpheus project

echo 🧪 Orpheus Test Suite Runner
echo ==============================

set SCRIPT_DIR=%~dp0
set PROJECT_ROOT=%SCRIPT_DIR%..

echo Project Root: %PROJECT_ROOT%
echo.

REM Check if we're in the right directory
if not exist "%PROJECT_ROOT%\Orpheus.csproj" (
    echo Error: Orpheus project not found. Make sure you're running this from the test project directory.
    exit /b 1
)

echo ----------------------------------------
echo Step 1: Building Solution
echo ----------------------------------------
cd /d "%PROJECT_ROOT%"
dotnet build Orpheus.sln --configuration Release
if errorlevel 1 (
    echo Error: Build failed
    exit /b 1
)
echo ✓ Build completed successfully

echo.
echo ----------------------------------------
echo Step 2: Running Unit Tests
echo ----------------------------------------
cd /d "%SCRIPT_DIR%"
dotnet test --configuration Release --verbosity normal --logger "console;verbosity=normal"
if errorlevel 1 (
    echo Error: Tests failed
    exit /b 1
)
echo ✓ Unit tests completed successfully

echo.
echo ----------------------------------------
echo Step 3: Running Tests with Coverage
echo ----------------------------------------
dotnet test --configuration Release --collect:"XPlat Code Coverage" --logger "console;verbosity=minimal"
if errorlevel 1 (
    echo Error: Coverage tests failed
    exit /b 1
)
echo ✓ Coverage tests completed successfully

echo.
echo ----------------------------------------
echo Test Summary
echo ----------------------------------------
echo ✓ All tests completed successfully!
echo.

REM Handle command line arguments
if "%1"=="--integration" (
    echo.
    echo ----------------------------------------
    echo Step 5: Running Integration Tests Only
    echo ----------------------------------------
    dotnet test --filter "FullyQualifiedName~Integration" --verbosity normal
)

if "%1"=="--unit" (
    echo.
    echo ----------------------------------------
    echo Step 5: Running Unit Tests Only
    echo ----------------------------------------
    dotnet test --filter "FullyQualifiedName!~Integration" --verbosity normal
)

echo.
echo 🎉 Test suite completed successfully!
echo Summary:
echo - Built solution
echo - Executed all tests
echo - Generated coverage report
echo.
echo To run specific test categories:
echo   run-tests.bat --unit        # Unit tests only
echo   run-tests.bat --integration # Integration tests only

pause