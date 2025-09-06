#!/bin/bash

# ============================================================
# BUILD AND START SERVER (Quick Development Mode)
# ============================================================
# This script ONLY builds and starts the server service.
# It does NOT run any tests.
#
# FOR FULL BUILD AND TEST VERIFICATION, USE:
#   ./build-and-test-all.sh
# ============================================================

# Default values
PORT=5099
ENVIRONMENT="Test"

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;37m'
NC='\033[0m' # No Color

# Function to print colored output
print_color() {
    echo -e "${1}${2}${NC}"
}

# Function to show usage
show_usage() {
    echo "Usage: $0 [OPTIONS]"
    echo "Options:"
    echo "  -p, --port PORT        Port number (default: 5099)"
    echo "  -e, --environment ENV  Environment setting (default: Test)"
    echo "  -h, --help            Show this help message"
    exit 1
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -p|--port)
            PORT="$2"
            shift 2
            ;;
        -e|--environment)
            ENVIRONMENT="$2"
            shift 2
            ;;
        -h|--help)
            show_usage
            ;;
        *)
            echo "Unknown option: $1"
            show_usage
            ;;
    esac
done

# Validate port is a number
if ! [[ "$PORT" =~ ^[0-9]+$ ]]; then
    print_color $RED "Error: Port must be a number"
    exit 1
fi

print_color $YELLOW "============================================================"
print_color $YELLOW " QUICK START: Server Build & Run (No Tests)"
print_color $YELLOW " For full build and test verification, use:"
print_color $CYAN " ./build-and-test-all.sh"
print_color $YELLOW "============================================================"
echo ""
print_color $GREEN "Building and starting server on port $PORT with environment $ENVIRONMENT..."

# 1. Take port that server is going to listen to (defaults to 5099), then search for it, and kill any process that may be
# listening on it.
print_color $YELLOW "Checking for processes listening on port $PORT..."

# Find processes listening on the port (works on both Linux and macOS)
if command -v lsof >/dev/null 2>&1; then
    # Use lsof if available (more reliable)
    PROCESSES=$(lsof -ti:$PORT 2>/dev/null)
elif command -v netstat >/dev/null 2>&1; then
    # Fallback to netstat
    PROCESSES=$(netstat -tlnp 2>/dev/null | grep ":$PORT " | awk '{print $7}' | cut -d'/' -f1)
else
    # Try ss as another fallback
    PROCESSES=$(ss -tlnp 2>/dev/null | grep ":$PORT " | awk '{print $6}' | grep -o 'pid=[0-9]*' | cut -d'=' -f2)
fi

if [ ! -z "$PROCESSES" ]; then
    print_color $YELLOW "Found processes listening on port $PORT:"
    for pid in $PROCESSES; do
        if [ ! -z "$pid" ] && [[ "$pid" =~ ^[0-9]+$ ]]; then
            print_color $GRAY "PID: $pid"
            if kill -0 $pid 2>/dev/null; then
                print_color $RED "Killing process with PID $pid..."
                if kill -TERM $pid 2>/dev/null; then
                    # Wait a bit for graceful shutdown
                    sleep 1
                    # If still running, force kill
                    if kill -0 $pid 2>/dev/null; then
                        kill -KILL $pid 2>/dev/null
                    fi
                    print_color $GREEN "Successfully killed process $pid"
                else
                    print_color $RED "Warning: Failed to kill process $pid"
                fi
            else
                print_color $GRAY "Process $pid already terminated"
            fi
        fi
    done

    # Wait a moment for ports to be released
    sleep 2
else
    print_color $GREEN "No processes found listening on port $PORT"
fi

# Ensure logs directory exists
LOGS_DIR="logs/server"
if [ ! -d "$LOGS_DIR" ]; then
    print_color $YELLOW "Creating logs directory: $LOGS_DIR"
    mkdir -p "$LOGS_DIR"
fi

# Clean existing log files
print_color $YELLOW "Cleaning existing log files..."
rm -f "$LOGS_DIR"/*.log 2>/dev/null || true
rm -f "$LOGS_DIR"/*.jsonl 2>/dev/null || true

# 2. Build the server project
print_color $YELLOW "Building server project..."
if dotnet build server/AIChat.Server.csproj --configuration Debug --verbosity minimal 2>&1 | tee "$LOGS_DIR/build.log"; then
    print_color $GREEN "Server build completed successfully"
else
    print_color $RED "Failed to build server"
    exit 1
fi

# 3. Start the server
print_color $YELLOW "Starting server on http://localhost:$PORT..."

# Set environment variables for the server
export ASPNETCORE_ENVIRONMENT="$ENVIRONMENT"
export ASPNETCORE_URLS="http://localhost:$PORT"
export LLM_API_KEY="DUMMY"

# Function to cleanup on exit
cleanup() {
    print_color $YELLOW "Cleaning up..."
    cd ..
    exit
}

# Set trap to cleanup on script exit
trap cleanup EXIT INT TERM

# Change to server directory and run the server with logging
cd server || {
    print_color $RED "Failed to change to server directory"
    exit 1
}

print_color $CYAN "Server starting with environment: $ENVIRONMENT"
print_color $CYAN "Server URL: http://localhost:$PORT"
print_color $CYAN "Build output logged to: ../logs/server/build.log"
print_color $CYAN "Server runtime logs will be appended to: ../logs/server/build.log"
print_color $YELLOW "Press Ctrl+C to stop the server"

# Run the server and append output to the same log file (after build output)
if ! dotnet run --project AIChat.Server.csproj --urls "http://localhost:$PORT" 2>&1 | tee -a "../logs/server/build.log"; then
    print_color $RED "Failed to start server"
    exit 1
fi
