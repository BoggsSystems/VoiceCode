#!/bin/bash

# VoiceCode Build Script
# Builds all services and creates Docker images

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
SOLUTION_FILE="VoiceCode.sln"
CONFIGURATION="${BUILD_CONFIGURATION:-Release}"
DOCKER_REGISTRY="${DOCKER_REGISTRY:-voicecodeprodacr.azurecr.io}"
VERSION="${VERSION:-latest}"

# Services to build
SERVICES=(
    "stt-service"
    "claude-service"
    "router-service"
    "generator-service"
    "tts-service"
    "dispatcher-service"
)

# Function to print colored output
print_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to check prerequisites
check_prerequisites() {
    print_info "Checking prerequisites..."
    
    # Check if .NET is installed
    if ! command -v dotnet &> /dev/null; then
        print_error ".NET SDK is not installed"
        exit 1
    fi
    
    # Check if Docker is installed
    if ! command -v docker &> /dev/null; then
        print_error "Docker is not installed"
        exit 1
    fi
    
    # Check .NET version
    DOTNET_VERSION=$(dotnet --version)
    print_info ".NET SDK version: $DOTNET_VERSION"
    
    # Check Docker version
    DOCKER_VERSION=$(docker --version)
    print_info "Docker version: $DOCKER_VERSION"
}

# Function to clean previous builds
clean_build() {
    print_info "Cleaning previous builds..."
    dotnet clean $SOLUTION_FILE --configuration $CONFIGURATION
    
    # Remove bin and obj directories
    find . -type d -name bin -exec rm -rf {} + 2>/dev/null || true
    find . -type d -name obj -exec rm -rf {} + 2>/dev/null || true
    
    print_success "Clean completed"
}

# Function to restore packages
restore_packages() {
    print_info "Restoring NuGet packages..."
    dotnet restore $SOLUTION_FILE
    print_success "Package restore completed"
}

# Function to build solution
build_solution() {
    print_info "Building solution..."
    dotnet build $SOLUTION_FILE --configuration $CONFIGURATION --no-restore
    
    if [ $? -eq 0 ]; then
        print_success "Build completed successfully"
    else
        print_error "Build failed"
        exit 1
    fi
}

# Function to run tests
run_tests() {
    print_info "Running tests..."
    dotnet test $SOLUTION_FILE \
        --configuration $CONFIGURATION \
        --no-build \
        --logger "console;verbosity=minimal" \
        --collect:"XPlat Code Coverage"
    
    if [ $? -eq 0 ]; then
        print_success "All tests passed"
    else
        print_error "Tests failed"
        exit 1
    fi
}

# Function to publish service
publish_service() {
    local service=$1
    print_info "Publishing $service..."
    
    dotnet publish "services/$service/VoiceCode.${service//-/}.csproj" \
        --configuration $CONFIGURATION \
        --no-build \
        --output "publish/$service"
    
    if [ $? -eq 0 ]; then
        print_success "$service published"
    else
        print_error "Failed to publish $service"
        exit 1
    fi
}

# Function to build Docker image
build_docker_image() {
    local service=$1
    local image_name="$DOCKER_REGISTRY/voicecode-$service:$VERSION"
    
    print_info "Building Docker image for $service..."
    
    docker build \
        -f "services/$service/Dockerfile" \
        -t "$image_name" \
        --build-arg BUILD_CONFIGURATION=$CONFIGURATION \
        .
    
    if [ $? -eq 0 ]; then
        print_success "Docker image built: $image_name"
        
        # Tag as latest if not already
        if [ "$VERSION" != "latest" ]; then
            docker tag "$image_name" "$DOCKER_REGISTRY/voicecode-$service:latest"
        fi
    else
        print_error "Failed to build Docker image for $service"
        exit 1
    fi
}

# Function to push Docker image
push_docker_image() {
    local service=$1
    local image_name="$DOCKER_REGISTRY/voicecode-$service:$VERSION"
    
    print_info "Pushing Docker image for $service..."
    
    docker push "$image_name"
    
    if [ $? -eq 0 ]; then
        print_success "Docker image pushed: $image_name"
        
        # Push latest tag as well
        if [ "$VERSION" != "latest" ]; then
            docker push "$DOCKER_REGISTRY/voicecode-$service:latest"
        fi
    else
        print_warning "Failed to push Docker image for $service (may need to login to registry)"
    fi
}

# Main execution
main() {
    local COMMAND=${1:-all}
    
    print_info "VoiceCode Build Script"
    print_info "Configuration: $CONFIGURATION"
    print_info "Docker Registry: $DOCKER_REGISTRY"
    print_info "Version: $VERSION"
    echo ""
    
    case $COMMAND in
        "all")
            check_prerequisites
            clean_build
            restore_packages
            build_solution
            run_tests
            
            # Publish all services
            for service in "${SERVICES[@]}"; do
                publish_service "$service"
            done
            
            # Build Docker images
            for service in "${SERVICES[@]}"; do
                build_docker_image "$service"
            done
            
            print_success "Build completed successfully!"
            ;;
            
        "build")
            check_prerequisites
            clean_build
            restore_packages
            build_solution
            ;;
            
        "test")
            run_tests
            ;;
            
        "docker")
            for service in "${SERVICES[@]}"; do
                build_docker_image "$service"
            done
            ;;
            
        "push")
            for service in "${SERVICES[@]}"; do
                push_docker_image "$service"
            done
            ;;
            
        "publish")
            for service in "${SERVICES[@]}"; do
                publish_service "$service"
            done
            ;;
            
        *)
            echo "Usage: $0 {all|build|test|docker|push|publish}"
            echo ""
            echo "Commands:"
            echo "  all     - Run complete build pipeline"
            echo "  build   - Build .NET solution only"
            echo "  test    - Run tests only"
            echo "  docker  - Build Docker images only"
            echo "  push    - Push Docker images to registry"
            echo "  publish - Publish services only"
            exit 1
            ;;
    esac
}

# Run main function
main "$@"