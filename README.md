# PowerShell API WebHookShell

[![.NET Webhook API Build and Release](https://github.com/sappkevin/PowerShell-API-Webhook/actions/workflows/main.yml/badge.svg)](https://github.com/sappkevin/PowerShell-API-Webhook/actions/workflows/main.yml)
[![Docker Multi-Architecture Build](https://github.com/sappkevin/PowerShell-API-Webhook/actions/workflows/docker-multi-arch-build.yml/badge.svg)](https://github.com/sappkevin/PowerShell-API-Webhook/actions/workflows/docker-multi-arch-build.yml)
[![Docker Hub](https://img.shields.io/docker/pulls/sappkevin/webhookshell.svg)](https://hub.docker.com/r/sappkevin/webhookshell)

# PowerShell-API-Webhook

You can find published binaries in the [releases section](https://github.com/sappkevin/PowerShell-API-Webhook/releases) and Docker images on [Docker Hub](https://hub.docker.com/r/sappkevin/webhookshell).

Cross-Platform .NET-based PowerShell API Webhook for turning PowerShell Scripts into API endpoints with background job processing capabilities.

## 🚀 Features
- Supports PowerShell 5.1 & PowerShell Core
- Cross-platform (Linux, macOS, Windows)
- Background job processing with Hangfire
- Scheduled script execution with CRON expressions
- Swagger UI for API documentation and testing
- Docker & Kubernetes support (Windows & Linux containers)
- Unit and performance tested for reliability
- Automated CI/CD with GitHub Actions
- Version-controlled builds
- Multi-threading support with concurrency limiting
- Rate limiting for high-traffic scenarios
- Script execution timeout controls

## 📦 Build & Deploy

### Build Locally:
```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build --configuration Release

# Run unit tests
dotnet test

# Publish DLL
dotnet publish -c Release -o ./publish/dll

# Publish EXE (Windows Self-Contained)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/exe
```

### Docker Support
You can use Docker to deploy the application:

```bash
# Build and run the Linux container
docker build -t webhookshell:latest -f Dockerfile.linux .
docker run -d -p 8080:80 -p 8443:443 --name webhookshell-linux webhookshell:latest

# Or use the pre-built image from Docker Hub
docker run -d -p 8080:80 -v ./scripts:/app/scripts sappkevin/webhookshell:latest
```

### Docker Compose
For easier deployment, use Docker Compose:

```bash
docker-compose up -d
```

## ⚡ How the API Works
- Exposes PowerShell scripts as API endpoints
- Supports GET/POST HTTP methods
- Secure API using API keys
- Async operations for non-blocking script executions
- Hangfire dashboard for monitoring background jobs
- Swagger UI available at the root URL for testing and documentation

### Example API Requests

#### Direct Script Execution:
```bash
# Using curl (GET)
curl -X GET "https://localhost:5001/webhook/v1?script=Test-Script.ps1&key=yourKey&parameters=-Param1+test+-Param2+sample"

# Using curl (POST)
curl -X POST https://localhost:5001/webhook/v1 \
  -H "Content-Type: application/json" \
  -d '{"script":"Test-Script.ps1","key":"yourKey","parameters":"-Param1 test -Param2 sample"}'

# Or use the Swagger UI in a browser
http://localhost:5001/
```

#### Background Job Processing:
```bash
# Enqueue a script to run in the background
curl -X POST https://localhost:5001/jobs/v1/enqueue \
  -H "Content-Type: application/json" \
  -d '{"script":"Test-Script.ps1","key":"yourKey","parameters":"-Param1 test -Param2 sample"}'

# Check job status
curl -X GET https://localhost:5001/jobs/v1/status/job123

# Access the Hangfire dashboard
http://localhost:5001/hangfire
```

## 🔧 Performance and Reliability
- Background job processing to handle long-running scripts
- Concurrency limits to prevent resource exhaustion
- Rate limiting to handle high-traffic scenarios
- Script execution timeouts (default: 5 minutes)
- Efficient process management with a job queue
- Performance testing with NBomber for high-load scenarios
- High-availability configuration for Kubernetes deployments

## 📆 Scheduling and Automation
You can configure recurring scripts in the `appsettings.json` file:

```json
"ScriptsMapping": [
  {
    "Name": "daily-cleanup.ps1",
    "Key": "your-api-key",
    "RecurringSchedule": "0 0 0 * * *",  // Runs at midnight daily
    "DefaultParameters": "-RetentionDays 30"
  },
  {
    "Name": "health-check.ps1",
    "Key": "your-api-key", 
    "RecurringSchedule": "0 */15 * * * *"  // Runs every 15 minutes
  }
]
```

CRON expressions are used to define the schedule, following the Hangfire format.

## 🗝️ Security & Authentication
- API Key validation via `appsettings.json`
- IP-based restrictions and execution time windows
- Hangfire dashboard authorization
- HTTPS redirection enabled by default

## Updates
### Version 1.1.0
- Added Hangfire for background job processing
- Added scheduled task execution with CRON expressions
- Added performance testing and rate limiting
- Added multi-architecture Docker support
- Added Docker Hub deployment pipeline

### Version 1.0.0
- Upgraded from .NET 6.0 to .NET 8.0 (LTS)
- Added multi-threading support
- Added CI/CD automation
- Added Swagger UI integration
- Added Docker and Kubernetes support

## 📜 Acknowledgements
The original project can be found [here](https://github.com/MTokarev/webhookshell).

- **Original Author:** [Mikhail Tokarev](https://github.com/MTokarev)

---
**License:** MIT