# PowerShell API WebHookShell Build Status

[![Build, Test, and Publish](https://github.com/sappkevin/PowerShell-API-Webhook/actions/workflows/build-and-deploy.yml/badge.svg)](https://github.com/sappkevin/PowerShell-API-Webhook/actions/workflows/build-and-deploy.yml)

# PowerShell-API-Webhook

Cross-Platform .NET-based PowerShell API Webhook for turning PowerShell Scripts into API endpoints.

## 🚀 Features
- Supports PowerShell 5.1 & PowerShell Core
- Cross-platform (Linux, macOS, Windows)
- Unit tested for reliability
- Automated CI/CD with GitHub Actions
- Version-controlled builds
- Multi-threading support via async/await for non-blocking operations
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

Artifacts can be downloaded from the **Actions** tab in GitHub.

### GitHub Actions Workflow Example
```yaml
name: Build, Test, and Publish
on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout repository
        uses: actions/checkout@v4
      - name: Setup .NET 8.0
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Restore dependencies
        run: dotnet restore
      - name: Build
        run: dotnet build --configuration Release
      - name: Test
        run: dotnet test
      - name: Publish DLL
        run: dotnet publish -c Release -o ./publish/dll
      - name: Publish EXE
        run: dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/exe
```

## 📥 Download Artifacts
1. Go to the **Actions** tab in GitHub.
2. Select the latest workflow run.
3. Download DLL or EXE from the artifacts section.

## ⚡ How the API Works
- Exposes PowerShell scripts as API endpoints.
- Supports GET/POST HTTP methods.
- Secure API using API keys.
- Async operations for non-blocking script executions.

### Example API Request
```bash
curl -X POST https://localhost:5001/webhook/v1 -d '{"key":"yourKey","script":"YourScript","param":"-Your-Params"}'
```

## 🗝️ Security & Authentication
- API Key validation via `appsettings.json`.
- IP-based restrictions and execution time windows supported.

## 📜 Acknowledgements 
The original project can be found [here](https://github.com/MTokarev/webhookshell).

- **Author:** [Mikhail Tokarev](https://github.com/MTokarev)
- **Updates:** Migrated from .NET 6.0 to .NET 8.0 (LTS).
- **Enhancements:** Added multi-threading support, CI/CD automation, and improved security measures.

---
**License:** MIT
