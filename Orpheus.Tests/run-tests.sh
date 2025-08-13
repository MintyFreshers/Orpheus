#!/bin/bash

# Orpheus Test Runner Script
# This script runs all tests for the Orpheus project

set -e  # Exit on any error

echo "🧪 Orpheus Test Suite Runner"
echo "=============================="

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Get script directory and project root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

echo -e "${BLUE}Project Root: $PROJECT_ROOT${NC}"
echo

# Function to print section headers
print_section() {
    echo -e "\n${YELLOW}$1${NC}"
    echo "----------------------------------------"
}

# Function to run command with error handling
run_command() {
    local description=$1
    shift
    echo -e "${BLUE}Running: $description${NC}"
    if "$@"; then
        echo -e "${GREEN}✓ $description completed successfully${NC}"
    else
        echo -e "${RED}✗ $description failed${NC}"
        exit 1
    fi
}

# Check if we're in the right directory
if [ ! -f "$PROJECT_ROOT/Orpheus.csproj" ]; then
    echo -e "${RED}Error: Orpheus project not found. Make sure you're running this from the test project directory.${NC}"
    exit 1
fi

print_section "Step 1: Building Solution"
cd "$PROJECT_ROOT"
run_command "Building Orpheus solution" dotnet build Orpheus.sln --configuration Release

print_section "Step 2: Running Unit Tests"
cd "$SCRIPT_DIR"
run_command "Running unit tests" dotnet test --configuration Release --verbosity normal --logger "console;verbosity=normal"

print_section "Step 3: Running Tests with Coverage"
run_command "Running tests with coverage" dotnet test --configuration Release --collect:"XPlat Code Coverage" --logger "console;verbosity=minimal"

print_section "Step 4: Test Summary"
echo -e "${GREEN}All tests completed successfully!${NC}"
echo

# Optional: Run specific test categories
if [ "$1" = "--integration" ]; then
    print_section "Step 5: Running Integration Tests Only"
    run_command "Running integration tests" dotnet test --filter "FullyQualifiedName~Integration" --verbosity normal
fi

if [ "$1" = "--unit" ]; then
    print_section "Step 5: Running Unit Tests Only"
    run_command "Running unit tests only" dotnet test --filter "FullyQualifiedName!~Integration" --verbosity normal
fi

echo -e "\n${GREEN}🎉 Test suite completed successfully!${NC}"
echo -e "${BLUE}Summary:${NC}"
echo "- Built solution"
echo "- Executed all tests"
echo "- Generated coverage report"
echo -e "\n${YELLOW}To run specific test categories:${NC}"
echo "  ./run-tests.sh --unit        # Unit tests only"
echo "  ./run-tests.sh --integration # Integration tests only"