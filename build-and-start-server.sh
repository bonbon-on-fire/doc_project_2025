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
USE_ORLEANS=false

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
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
    echo "  -o, --orleans         Enable Orleans (forces Development environment if Test)"
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
        -o|--orleans)
            USE_ORLEANS=true
            shift
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

# Handle Orleans flag - automatically switch to Development environment if USE_ORLEANS is set
if [ "$USE_ORLEANS" = true ]; then
    if [ "$ENVIRONMENT" = "Test" ]; then
        print_color $MAGENTA "Orleans requested: Switching from Test to Development environment"
        print_color $YELLOW "Note: Orleans is disabled in Test environment by design"
        ENVIRONMENT="Development"
    fi
    print_color $CYAN "Orleans Integration: ENABLED (via --orleans flag)"
    print_color $CYAN "Environment: $ENVIRONMENT"
    print_color $CYAN "Orleans Dashboard will be available at: http://localhost:8081"
else
    if [ "$ENVIRONMENT" = "Development" ] || [ "$ENVIRONMENT" = "Production" ]; then
        print_color $YELLOW "Note: Orleans may be enabled based on $ENVIRONMENT environment settings"
        print_color $YELLOW "To ensure Orleans is disabled, use Test environment or check appsettings.$ENVIRONMENT.json"
    else
        print_color $YELLOW "Orleans Integration: DISABLED (Test environment)"
    fi
fi

echo ""
print_color $GREEN "Building and starting server on port $PORT with environment $ENVIRONMENT..."

# 1. Take port that server is going to listen to (defaults to 5099), then search for it, and kill any process that may be
# listening on it.
print_color $YELLOW "Checking for processes listening on port $PORT..."

# If Orleans is enabled, also check Orleans-specific ports
if [ "$USE_ORLEANS" = true ]; then
    print_color $YELLOW "Checking Orleans ports (30000, 11111, 8081)..."
    ORLEANS_PORTS="30000 11111 8081"
    for oport in $ORLEANS_PORTS; do
        if command -v lsof >/dev/null 2>&1; then
            ORLEANS_PROCESSES=$(lsof -ti:$oport 2>/dev/null)
        elif command -v netstat >/dev/null 2>&1; then
            ORLEANS_PROCESSES=$(netstat -tlnp 2>/dev/null | grep ":$oport " | awk '{print $7}' | cut -d'/' -f1)
        else
            ORLEANS_PROCESSES=$(ss -tlnp 2>/dev/null | grep ":$oport " | awk '{print $6}' | grep -o 'pid=[0-9]*' | cut -d'=' -f2)
        fi
        
        if [ ! -z "$ORLEANS_PROCESSES" ]; then
            print_color $YELLOW "Found processes on Orleans port $oport:"
            for pid in $ORLEANS_PROCESSES; do
                if [ ! -z "$pid" ] && [[ "$pid" =~ ^[0-9]+$ ]]; then
                    print_color $GRAY "PID: $pid"
                    print_color $RED "Killing Orleans-related process with PID $pid..."
                    if kill -TERM $pid 2>/dev/null; then
                        sleep 1
                        if kill -0 $pid 2>/dev/null; then
                            kill -KILL $pid 2>/dev/null
                        fi
                        print_color $GREEN "Successfully killed process $pid"
                    else
                        print_color $RED "Warning: Failed to kill process $pid"
                    fi
                fi
            done
        fi
    done
fi

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

# Ensure logs directories exist
SERVER_LOGS_DIR="logs/server"
CLIENT_LOGS_DIR="logs/client"
if [ ! -d "$SERVER_LOGS_DIR" ]; then
    print_color $YELLOW "Creating server logs directory: $SERVER_LOGS_DIR"
    mkdir -p "$SERVER_LOGS_DIR"
fi
if [ ! -d "$CLIENT_LOGS_DIR" ]; then
    print_color $YELLOW "Creating client logs directory: $CLIENT_LOGS_DIR"
    mkdir -p "$CLIENT_LOGS_DIR"
fi

# Clean existing log files (both server and client)
print_color $YELLOW "Cleaning existing server and client log files..."
rm -f "$SERVER_LOGS_DIR"/*.log 2>/dev/null || true
rm -f "$SERVER_LOGS_DIR"/*.jsonl 2>/dev/null || true
rm -f "$CLIENT_LOGS_DIR"/*.log 2>/dev/null || true
rm -f "$CLIENT_LOGS_DIR"/*.jsonl 2>/dev/null || true

# 2. Build the server project
print_color $YELLOW "Building server project..."
if dotnet build server/AIChat.Server/AIChat.Server.csproj --configuration Debug --verbosity minimal 2>&1 | tee "$SERVER_LOGS_DIR/build.log"; then
    print_color $GREEN "Server build completed successfully"
else
    print_color $RED "Failed to build server"
    exit 1
fi

# 3. If Orleans is enabled, handle based on environment
ORLEANS_PID=""
if [ "$USE_ORLEANS" = true ]; then
    if [ "$ENVIRONMENT" = "Development" ] || [ "$ENVIRONMENT" = "Test" ]; then
        # In Development/Test, Orleans is co-hosted within the server process
        echo ""
        print_color $CYAN "Orleans will be co-hosted within the server process (single-process mode)"
        print_color $GREEN "No separate Orleans Host needed for $ENVIRONMENT environment"
    else
        # In Production, start Orleans Host as separate process
        echo ""
        print_color $YELLOW "Building Orleans Host for Production environment..."
        
        # Build Orleans Host project
        if dotnet build server/AIChat.Orleans.Host/AIChat.Orleans.Host.csproj --configuration Debug --verbosity minimal 2>&1 | tee "$SERVER_LOGS_DIR/orleans-build.log"; then
            print_color $GREEN "Orleans Host build completed successfully"
            
            # Start Orleans Host in background
            print_color $YELLOW "Starting Orleans Host in background..."
            dotnet run --project server/AIChat.Orleans.Host/AIChat.Orleans.Host.csproj --no-build > "$SERVER_LOGS_DIR/orleans-output.log" 2> "$SERVER_LOGS_DIR/orleans-error.log" &
            ORLEANS_PID=$!
            
            # Wait a moment for Orleans to start
            print_color $YELLOW "Waiting for Orleans Silo to initialize..."
            sleep 5
            
            # Check if Orleans Host is still running
            if kill -0 $ORLEANS_PID 2>/dev/null; then
                print_color $GREEN "Orleans Host started successfully (PID: $ORLEANS_PID)"
                print_color $CYAN "Orleans Dashboard: http://localhost:8081"
            else
                print_color $RED "Orleans Host failed to start. Check logs/server/orleans-error.log for details"
                exit 1
            fi
        else
            print_color $RED "Failed to build Orleans Host"
            exit 1
        fi
    fi
fi

# 4. Start the server
print_color $YELLOW "Starting server on http://localhost:$PORT..."

# Set environment variables for the server
export ASPNETCORE_ENVIRONMENT="$ENVIRONMENT"
export ASPNETCORE_URLS="http://localhost:$PORT"
export LLM_API_KEY="DUMMY"

# Function to cleanup on exit
cleanup() {
    print_color $YELLOW "Cleaning up..."
    
    # Stop Orleans Host if it was started
    if [ ! -z "$ORLEANS_PID" ] && kill -0 $ORLEANS_PID 2>/dev/null; then
        print_color $YELLOW "Stopping Orleans Host (PID: $ORLEANS_PID)..."
        kill -TERM $ORLEANS_PID 2>/dev/null
        sleep 1
        if kill -0 $ORLEANS_PID 2>/dev/null; then
            kill -KILL $ORLEANS_PID 2>/dev/null
        fi
        print_color $GREEN "Orleans Host stopped"
    fi
    
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

echo ""
print_color $GREEN "============================================================"
print_color $CYAN "Server Configuration:"
print_color $CYAN "  Environment: $ENVIRONMENT"
print_color $CYAN "  Server URL: http://localhost:$PORT"
if [ "$USE_ORLEANS" = true ]; then
    if [ "$ENVIRONMENT" = "Development" ] || [ "$ENVIRONMENT" = "Test" ]; then
        print_color $GREEN "  Orleans: ENABLED (Co-hosted mode)"
        print_color $GREEN "  Orleans Dashboard: Integrated with server"
    else
        print_color $GREEN "  Orleans: ENABLED (Separate process)"
        print_color $GREEN "  Orleans Dashboard: http://localhost:8081"
    fi
    print_color $CYAN "  Orleans Gateway Port: 30000"
    print_color $CYAN "  Orleans Silo Port: 11111"
else
    if [ "$ENVIRONMENT" = "Test" ]; then
        print_color $YELLOW "  Orleans: DISABLED (Test environment)"
    else
        print_color $YELLOW "  Orleans: Check appsettings.$ENVIRONMENT.json for status"
    fi
fi
print_color $GREEN "============================================================"
echo ""
print_color $CYAN "Build output logged to: logs/server/build.log"
print_color $CYAN "Server runtime logs will be appended to: logs/server/build.log"
print_color $CYAN "Application logs: logs/server/app-${ENVIRONMENT}.jsonl"
echo ""
print_color $YELLOW "Press Ctrl+C to stop the server"
echo ""

# Run the server and append output to the same log file (after build output)
# Using launch profiles for proper environment configuration
if [ "$USE_ORLEANS" = true ]; then
    # Use DEV profile when Orleans is enabled (Orleans requires Development environment)
    if ! dotnet run --project AIChat.Server/AIChat.Server.csproj --launch-profile DEV --urls "http://localhost:$PORT" 2>&1 | tee -a "../logs/server/build.log"; then
        print_color $RED "Failed to start server"
        exit 1
    fi
else
    # Use TEST profile by default (or specify based on Environment parameter)
    PROFILE="TEST"
    if [ "$ENVIRONMENT" = "Development" ]; then
        PROFILE="DEV"
    fi
    if ! dotnet run --project AIChat.Server/AIChat.Server.csproj --launch-profile "$PROFILE" --urls "http://localhost:$PORT" 2>&1 | tee -a "../logs/server/build.log"; then
        print_color $RED "Failed to start server"
        exit 1
    fi
fi
