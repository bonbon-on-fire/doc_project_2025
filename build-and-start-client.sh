#!/bin/bash

# ============================================================
# BUILD AND START CLIENT (Quick Development Mode)
# ============================================================
# This script ONLY builds and starts the client service.
# It does NOT run any tests.
#
# FOR FULL BUILD AND TEST VERIFICATION, USE:
#   ./build-and-test-all.sh
# ============================================================

# Default values
PORT=5173
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
    echo "  -p, --port PORT        Port number (default: 5173)"
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
print_color $YELLOW " QUICK START: Client Build & Run (No Tests)"
print_color $YELLOW " For full build and test verification, use:"
print_color $CYAN " ./build-and-test-all.sh"
print_color $YELLOW "============================================================"
echo ""
print_color $GREEN "Building and starting client on port $PORT with environment $ENVIRONMENT..."

# 1. Take port that client is going to listen to (defaults to 5173), then search for it, and kill any process that may be
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
LOGS_DIR="logs/client"
if [ ! -d "$LOGS_DIR" ]; then
    print_color $YELLOW "Creating logs directory: $LOGS_DIR"
    mkdir -p "$LOGS_DIR"
fi

# Clean existing log files
print_color $YELLOW "Cleaning existing log files..."
rm -f "$LOGS_DIR"/*.log 2>/dev/null || true
rm -f "$LOGS_DIR"/*.jsonl 2>/dev/null || true

# 2. Install dependencies and build the client project
print_color $YELLOW "Installing client dependencies..."

# Change to client directory
cd client || {
    print_color $RED "Failed to change to client directory"
    exit 1
}

if npm install 2>&1 | tee "../$LOGS_DIR/build.log"; then
    print_color $GREEN "Client dependencies installed successfully"
else
    print_color $RED "Failed to install client dependencies"
    cd ..
    exit 1
fi

# 3. Start the client
print_color $YELLOW "Starting client on http://localhost:$PORT..."

# Determine which npm script to run based on environment
if [ "$ENVIRONMENT" = "Test" ]; then
    NPM_SCRIPT="dev:test"
else
    NPM_SCRIPT="dev"
fi

# Function to cleanup on exit
cleanup() {
    print_color $YELLOW "Cleaning up..."
    cd ..
    exit
}

# Set trap to cleanup on script exit
trap cleanup EXIT INT TERM

print_color $CYAN "Client starting with environment: $ENVIRONMENT"
print_color $CYAN "Client URL: http://localhost:$PORT"
print_color $CYAN "NPM script: $NPM_SCRIPT"
print_color $CYAN "Dependencies logged to: ../$LOGS_DIR/build.log"
print_color $CYAN "Client runtime logs will be appended to: ../$LOGS_DIR/build.log"
print_color $YELLOW "Press Ctrl+C to stop the client"

# Run the client and append output to the same log file (after npm install output)
if ! npm run "$NPM_SCRIPT" 2>&1 | tee -a "../$LOGS_DIR/build.log"; then
    print_color $RED "Failed to start client"
    exit 1
fi
