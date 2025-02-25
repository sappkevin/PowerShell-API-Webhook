# PowerShell API WebHookShell Build Status

[![.NET Webhook API Build and Release](https://github.com/sappkevin/PowerShell-API-Webhook/actions/workflows/main.yml/badge.svg)](https://github.com/sappkevin/PowerShell-API-Webhook/actions/workflows/main.yml)

# PowerShell-API-Webhook

Currently, you can find published binaries [here](https://github.com/sappkevin/PowerShell-API-Webhook/releases)

Cross-Platform .NET-based PowerShell API Webhook for turning PowerShell Scripts into API endpoints.

## 🚀 Features
- Supports PowerShell 5.1 & PowerShell Core
- Cross-platform (Linux, macOS, Windows)
- Swagger UI for API documentation and testing
- Docker & Kubernetes support (Windows containers)
- Unit tested for reliability
- Automated CI/CD with GitHub Actions
- Version-controlled builds
- Multi-threading support with concurrency limiting
- Script execution timeout controls
- Fixed issues from the original codebase (improved performance and stability)

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

### CI/CD Pipeline
The GitHub Actions workflow:
- **Builds** the project on every push to `main`
- **Runs unit tests**
- **Publishes** both DLL and EXE artifacts with version numbers

### Docker Support
You can use Docker to deploy the application as a Windows container:

```bash
# Build the Docker image
docker build -t webhookshell:latest -f Dockerfile.windows .

# Run the container
docker run -d -p 8080:80 -p 8443:443 --name webhookshell webhookshell:latest
```

### Docker Compose
For easier deployment, use Docker Compose:

```bash
docker-compose up -d
```

### Kubernetes Deployment
Deploy to a Kubernetes cluster with Windows nodes:

```bash
kubectl apply -f kubernetes-deployment.yaml
```

## 📥 Download Artifacts
- Artifacts can be downloaded from **Releases** [link](https://github.com/sappkevin/PowerShell-API-Webhook/releases)

## ⚡ How the API Works
- Exposes PowerShell scripts as API endpoints
- Supports GET/POST HTTP methods
- Secure API using API keys
- Async operations for non-blocking script executions
- Swagger UI available at the root URL for testing and documentation

### Example API Request
```bash
# Using curl
curl -X POST https://localhost:5001/webhook/v1 -d '{"key":"yourKey","script":"YourScript","param":"-Your-Params"}'

# Or use the Swagger UI in a browser
http://localhost:5001/
```

## 🔧 Performance Optimizations
- Concurrency limits to prevent resource exhaustion
- Script execution timeouts (default: 5 minutes)
- Efficient process management
- Singleton service registration for improved performance
- Handles high request-per-second workloads

## 🗝️ Security & Authentication
- API Key validation via `appsettings.json`
- IP-based restrictions and execution time windows supported
- HTTPS redirection enabled by default

## Updates
### Upgraded from .NET 6.0 to .NET 8.0 (LTS)
- **Enhancements:**
  - Added multi-threading support
  - CI/CD automation
  - Improved security measures
  - Swagger UI integration
  - Docker and Kubernetes support
  - Performance optimizations


## 📜 Acknowledgements
The original project can be found [here](https://github.com/MTokarev/webhookshell).

- **Author:** [Mikhail Tokarev](https://github.com/MTokarev)

---
**License:** MIT
