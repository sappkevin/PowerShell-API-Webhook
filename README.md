# PowerShell-API-Webhook
Cross Platform .Net based PowerShell API Webhook for turning PowerShell Scripts into API endpoints# Webhookshell

This cross-platform script execution service is built with **.NET 8.0 (LTS)**, supporting both **DLL** and **EXE** deployments.

## 🚀 Features
- Supports PowerShell 5.1 & PowerShell Core
- Cross-platform (Linux, macOS, Windows)
- Unit tested for reliability
- Automated CI/CD with GitHub Actions
- Version-controlled builds

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

### CI/CD Pipeline:
The GitHub Actions workflow:
- **Builds** the project on every push to `main`
- **Runs unit tests**
- **Publishes** both DLL and EXE artifacts with version numbers

Artifacts can be downloaded from the **Actions** tab in GitHub.

## 📥 Download Artifacts
1. Go to **Actions** tab
2. Select the latest workflow run
3. Download DLL or EXE from the artifacts section

## 📜 Acknowledgements for Original Project Source Code
The original project can be found [here](https://github.com/MTokarev/webhookshell).
-- Author: [Mikhail Tokarev](https://github.com/MTokarev)
-- .NET 6.0 was deprecated so this code is used to update supported .Net version to .NET 8.0 (LTS) 

---
**License:** MIT

