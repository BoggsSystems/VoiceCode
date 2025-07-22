#!/bin/bash

# VoiceCode Local Development Script
# Helps run services locally for development

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
SOLUTION_FILE="VoiceCode.sln"
DOCKER_COMPOSE_FILE="docker-compose.yml"

# Services
SERVICES=(
    "stt-service:5001"
    "claude-service:5002"
    "router-service:5003"
    "generator-service:5004"
    "tts-service:5005"
    "dispatcher-service:5006"
)

# Function to print colored output
print_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check dependencies
check_dependencies() {
    print_info "Checking dependencies..."
    
    # Check Docker
    if ! command -v docker &> /dev/null; then
        print_error "Docker is not installed"
        exit 1
    fi
    
    # Check Docker Compose
    if ! command -v docker-compose &> /dev/null; then
        print_error "Docker Compose is not installed"
        exit 1
    fi
    
    # Check .NET
    if ! command -v dotnet &> /dev/null; then
        print_error ".NET SDK is not installed"
        exit 1
    fi
    
    print_success "All dependencies are installed"
}

# Start infrastructure services
start_infrastructure() {
    print_info "Starting infrastructure services..."
    
    # Create docker-compose for local infrastructure
    cat > docker-compose.yml << EOF
version: '3.8'

services:
  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis-data:/data

  azurite:
    image: mcr.microsoft.com/azure-storage/azurite
    ports:
      - "10000:10000"  # Blob service
      - "10001:10001"  # Queue service
      - "10002:10002"  # Table service
    volumes:
      - azurite-data:/data

  cosmos-emulator:
    image: mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator
    ports:
      - "8081:8081"
      - "10251:10251"
      - "10252:10252"
      - "10253:10253"
      - "10254:10254"
    environment:
      - AZURE_COSMOS_EMULATOR_PARTITION_COUNT=10
      - AZURE_COSMOS_EMULATOR_ENABLE_DATA_PERSISTENCE=true
    volumes:
      - cosmos-data:/data

volumes:
  redis-data:
  azurite-data:
  cosmos-data:
EOF

    docker-compose up -d
    
    print_success "Infrastructure services started"
    print_info "Redis: localhost:6379"
    print_info "Azurite Blob: localhost:10000"
    print_info "Cosmos Emulator: https://localhost:8081"
}

# Create local settings
create_local_settings() {
    print_info "Creating local development settings..."
    
    # Create local.settings.json for each service
    for service_port in "${SERVICES[@]}"; do
        IFS=':' read -r service port <<< "$service_port"
        
        cat > "services/$service/appsettings.Local.json" << EOF
{
  "ConnectionStrings": {
    "ServiceBus": "UseDevelopmentStorage=true",
    "Redis": "localhost:6379",
    "Storage": "UseDevelopmentStorage=true",
    "CosmosDb": "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
  },
  "AzureSpeech": {
    "Key": "your-speech-key",
    "Region": "eastus"
  },
  "Anthropic": {
    "ApiKey": "your-claude-key",
    "BaseUrl": "https://api.anthropic.com/v1"
  },
  "OpenAI": {
    "ApiKey": "your-openai-key"
  },
  "ServiceEndpoints": {
    "STTService": "http://localhost:5001",
    "ClaudeService": "http://localhost:5002",
    "RouterService": "http://localhost:5003",
    "GeneratorService": "http://localhost:5004",
    "TTSService": "http://localhost:5005"
  },
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=00000000-0000-0000-0000-000000000000"
  }
}
EOF
    done
    
    print_success "Local settings created"
}

# Run a single service
run_service() {
    local service=$1
    local port=$2
    
    print_info "Starting $service on port $port..."
    
    cd "services/$service"
    dotnet run --urls="http://localhost:$port;https://localhost:$((port + 1000))" &
    cd ../..
}

# Run all services
run_all_services() {
    print_info "Starting all services..."
    
    # Build solution first
    dotnet build $SOLUTION_FILE --configuration Debug
    
    # Start each service
    for service_port in "${SERVICES[@]}"; do
        IFS=':' read -r service port <<< "$service_port"
        run_service $service $port
    done
    
    print_success "All services started"
    echo ""
    print_info "Service URLs:"
    for service_port in "${SERVICES[@]}"; do
        IFS=':' read -r service port <<< "$service_port"
        echo "  $service: http://localhost:$port"
    done
    echo ""
    print_info "Press Ctrl+C to stop all services"
    
    # Wait for interrupt
    wait
}

# Stop all services
stop_all() {
    print_info "Stopping all services..."
    
    # Stop .NET processes
    pkill -f "dotnet run" || true
    
    # Stop infrastructure
    docker-compose down
    
    print_success "All services stopped"
}

# Clean up
cleanup() {
    print_info "Cleaning up..."
    
    # Remove local settings
    find services -name "appsettings.Local.json" -delete
    
    # Remove docker-compose file
    rm -f docker-compose.yml
    
    # Clean build artifacts
    dotnet clean $SOLUTION_FILE
    
    print_success "Cleanup completed"
}

# Run integration tests locally
run_local_tests() {
    print_info "Running integration tests..."
    
    dotnet test tests/VoiceCode.IntegrationTests \
        --configuration Debug \
        --logger "console;verbosity=normal" \
        -- xunit.parallelExecution=false
}

# Main execution
main() {
    local COMMAND=${1:-start}
    
    case $COMMAND in
        "start")
            check_dependencies
            start_infrastructure
            create_local_settings
            
            # Give infrastructure time to start
            print_info "Waiting for infrastructure to be ready..."
            sleep 10
            
            run_all_services
            ;;
            
        "stop")
            stop_all
            ;;
            
        "restart")
            stop_all
            sleep 2
            main start
            ;;
            
        "infra")
            check_dependencies
            start_infrastructure
            ;;
            
        "test")
            run_local_tests
            ;;
            
        "clean")
            stop_all
            cleanup
            ;;
            
        "logs")
            local service=${2:-"all"}
            if [ "$service" == "all" ]; then
                docker-compose logs -f
            else
                docker-compose logs -f $service
            fi
            ;;
            
        *)
            echo "Usage: $0 {start|stop|restart|infra|test|clean|logs [service]}"
            echo ""
            echo "Commands:"
            echo "  start    - Start all services and infrastructure"
            echo "  stop     - Stop all services and infrastructure"
            echo "  restart  - Restart all services"
            echo "  infra    - Start only infrastructure services"
            echo "  test     - Run integration tests"
            echo "  clean    - Stop everything and clean up"
            echo "  logs     - View logs (optionally specify service)"
            exit 1
            ;;
    esac
}

# Trap Ctrl+C
trap 'stop_all' INT

# Run main function
main "$@"