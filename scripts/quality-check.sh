#!/bin/bash

# quality-check.sh - Complete quality validation script for Orleans migration
# This script runs all quality gates mentioned in tasks.md
# Usage: ./scripts/quality-check.sh

set -e  # Exit on any error

echo "🔍 Orleans Migration Quality Check"
echo "=================================="
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Track overall status
OVERALL_SUCCESS=true

# Function to print status
print_status() {
    if [ $1 -eq 0 ]; then
        echo -e "${GREEN}✅ $2${NC}"
    else
        echo -e "${RED}❌ $2${NC}"
        OVERALL_SUCCESS=false
    fi
}

# Function to print section header
print_section() {
    echo ""
    echo -e "${BLUE}🔨 $1${NC}"
    echo "----------------------------------------"
}

# 1. BUILD VALIDATION
print_section "Build Validation"

echo "Cleaning previous builds..."
dotnet clean > /dev/null 2>&1
print_status $? "Clean completed"

echo "Restoring NuGet packages..."
dotnet restore > /dev/null 2>&1
print_status $? "Package restore"

echo "Building Debug configuration..."
dotnet build --configuration Debug --no-restore > /dev/null 2>&1
BUILD_DEBUG_STATUS=$?
print_status $BUILD_DEBUG_STATUS "Debug build"

echo "Building Release configuration..."
dotnet build --configuration Release --no-restore > /dev/null 2>&1
BUILD_RELEASE_STATUS=$?
print_status $BUILD_RELEASE_STATUS "Release build"

if [ $BUILD_DEBUG_STATUS -ne 0 ] || [ $BUILD_RELEASE_STATUS -ne 0 ]; then
    echo -e "${RED}Build failed. Please fix build errors before continuing.${NC}"
    exit 1
fi

# 2. TEST EXECUTION
print_section "Test Execution"

echo "Running all tests..."
dotnet test --configuration Release --no-build --collect:"XPlat Code Coverage" --logger "console;verbosity=minimal" > /dev/null 2>&1
TEST_STATUS=$?
print_status $TEST_STATUS "All tests"

if [ $TEST_STATUS -ne 0 ]; then
    echo -e "${YELLOW}Some tests failed. Running tests with verbose output...${NC}"
    dotnet test --configuration Release --no-build --logger "console;verbosity=normal"
fi

# 3. CODE QUALITY
print_section "Code Quality"

echo "Checking code formatting..."
dotnet format --verify-no-changes --verbosity quiet > /dev/null 2>&1
FORMAT_STATUS=$?
print_status $FORMAT_STATUS "Code formatting"

if [ $FORMAT_STATUS -ne 0 ]; then
    echo -e "${YELLOW}Code formatting issues found. Run 'dotnet format' to fix.${NC}"
fi

echo "Running static analysis..."
dotnet build /p:RunAnalyzers=true /p:TreatWarningsAsErrors=true --no-restore --verbosity quiet > /dev/null 2>&1
ANALYZER_STATUS=$?
print_status $ANALYZER_STATUS "Static analysis"

# 4. SECURITY SCAN
print_section "Security Scan"

echo "Scanning for vulnerable packages..."
VULNERABLE_OUTPUT=$(dotnet list package --vulnerable 2>/dev/null)
if echo "$VULNERABLE_OUTPUT" | grep -q "has the following vulnerable packages"; then
    echo -e "${RED}❌ Vulnerable packages found:${NC}"
    echo "$VULNERABLE_OUTPUT"
    SECURITY_STATUS=1
else
    SECURITY_STATUS=0
fi
print_status $SECURITY_STATUS "Package security scan"

# 5. ORLEANS RUNTIME VALIDATION (if Orleans projects exist)
if [ -d "AIChat.Orleans.Host" ]; then
    print_section "Orleans Runtime Validation"
    
    echo "Testing Orleans silo startup..."
    cd AIChat.Orleans.Host
    
    # Start Orleans silo in background
    timeout 30s dotnet run > /dev/null 2>&1 &
    SILO_PID=$!
    
    # Wait for startup
    sleep 20
    
    # Check if silo is running
    if kill -0 $SILO_PID 2>/dev/null; then
        echo "Testing health endpoint..."
        if curl -f http://localhost:5100/health > /dev/null 2>&1; then
            ORLEANS_STATUS=0
        else
            ORLEANS_STATUS=1
        fi
        
        # Clean up
        kill $SILO_PID 2>/dev/null || true
        wait $SILO_PID 2>/dev/null || true
    else
        ORLEANS_STATUS=1
    fi
    
    cd ..
    print_status $ORLEANS_STATUS "Orleans silo health"
fi

# 6. SUMMARY
print_section "Quality Check Summary"

if [ "$OVERALL_SUCCESS" = true ] && [ $TEST_STATUS -eq 0 ] && [ $SECURITY_STATUS -eq 0 ]; then
    echo -e "${GREEN}🎉 ALL QUALITY GATES PASSED!${NC}"
    echo -e "${GREEN}✅ Ready for task completion or commit${NC}"
    exit 0
else
    echo -e "${RED}❌ QUALITY GATES FAILED${NC}"
    echo ""
    echo -e "${YELLOW}Failed checks:${NC}"
    
    if [ $BUILD_DEBUG_STATUS -ne 0 ] || [ $BUILD_RELEASE_STATUS -ne 0 ]; then
        echo "• Build validation failed"
    fi
    
    if [ $TEST_STATUS -ne 0 ]; then
        echo "• Test execution failed"
    fi
    
    if [ $FORMAT_STATUS -ne 0 ]; then
        echo "• Code formatting issues"
    fi
    
    if [ $ANALYZER_STATUS -ne 0 ]; then
        echo "• Static analysis warnings"
    fi
    
    if [ $SECURITY_STATUS -ne 0 ]; then
        echo "• Security vulnerabilities found"
    fi
    
    if [ "${ORLEANS_STATUS:-0}" -ne 0 ]; then
        echo "• Orleans runtime issues"
    fi
    
    echo ""
    echo -e "${YELLOW}Fix the issues above before marking task complete or committing.${NC}"
    exit 1
fi