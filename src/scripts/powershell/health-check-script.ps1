param(
    [string]$Component = "all",
    [string]$OutputFormat = "json",
    [switch]$Detailed
)

<#
.SYNOPSIS
Health check script for webhook API monitoring

.DESCRIPTION
Performs comprehensive health checks on the webhook API system including:
- System resources (CPU, Memory, Disk)
- Network connectivity
- PowerShell availability
- DNS resolution
- Service dependencies
- Custom application checks

.PARAMETER Component
Specific component to check: all, system, network, powershell, dns, services, app

.PARAMETER OutputFormat
Output format: json, text, prometheus

.PARAMETER Detailed
Include detailed diagnostic information

.EXAMPLE
.\health-check-script.ps1 -Component all -OutputFormat json

.EXAMPLE
.\health-check-script.ps1 -Component system -Detailed
#>

# Initialize health check results
$healthResults = @{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss UTC"
    OverallStatus = "Healthy"
    Checks = @()
    Summary = @{
        Total = 0
        Passed = 0
        Failed = 0
        Warnings = 0
    }
    SystemInfo = @{
        Hostname = $env:COMPUTERNAME
        OS = "$($PSVersionTable.OS)"
        PowerShellVersion = "$($PSVersionTable.PSVersion)"
        Architecture = "$($env:PROCESSOR_ARCHITECTURE)"
    }
}

# Function to add a health check result
function Add-HealthCheck {
    param(
        [string]$Name,
        [string]$Status,      # Healthy, Warning, Unhealthy
        [string]$Message,
        [hashtable]$Details = @{},
        [string]$Component = "General"
    )
    
    $check = @{
        Name = $Name
        Component = $Component
        Status = $Status
        Message = $Message
        Details = $Details
        Timestamp = Get-Date -Format "HH:mm:ss"
    }
    
    $healthResults.Checks += $check
    $healthResults.Summary.Total++
    
    switch ($Status) {
        "Healthy" { $healthResults.Summary.Passed++ }
        "Warning" { $healthResults.Summary.Warnings++ }
        "Unhealthy" { 
            $healthResults.Summary.Failed++
            $healthResults.OverallStatus = "Unhealthy"
        }
    }
    
    Write-Output "$($check.Timestamp) [$Status] $Name - $Message"
}

# System Resource Checks
function Test-SystemResources {
    if ($Component -eq "all" -or $Component -eq "system") {
        Write-Output "Checking system resources..."
        
        try {
            # CPU Usage
            $cpu = Get-CimInstance -ClassName Win32_Processor | Measure-Object -Property LoadPercentage -Average
            $cpuUsage = [math]::Round($cpu.Average, 2)
            
            $cpuStatus = if ($cpuUsage -lt 80) { "Healthy" } elseif ($cpuUsage -lt 95) { "Warning" } else { "Unhealthy" }
            Add-HealthCheck -Name "CPU Usage" -Status $cpuStatus -Message "$cpuUsage% average load" -Component "System" -Details @{ Usage = $cpuUsage; Threshold = 80 }
            
            # Memory Usage
            $memory = Get-CimInstance -ClassName Win32_OperatingSystem
            $memoryUsagePercent = [math]::Round((($memory.TotalVisibleMemorySize - $memory.FreePhysicalMemory) / $memory.TotalVisibleMemorySize) * 100, 2)
            $memoryUsageGB = [math]::Round(($memory.TotalVisibleMemorySize - $memory.FreePhysicalMemory) / 1MB, 2)
            $totalMemoryGB = [math]::Round($memory.TotalVisibleMemorySize / 1MB, 2)
            
            $memoryStatus = if ($memoryUsagePercent -lt 85) { "Healthy" } elseif ($memoryUsagePercent -lt 95) { "Warning" } else { "Unhealthy" }
            Add-HealthCheck -Name "Memory Usage" -Status $memoryStatus -Message "$memoryUsagePercent% ($memoryUsageGB GB / $totalMemoryGB GB)" -Component "System" -Details @{ UsagePercent = $memoryUsagePercent; UsageGB = $memoryUsageGB; TotalGB = $totalMemoryGB }
            
            # Disk Space
            $disks = Get-CimInstance -ClassName Win32_LogicalDisk | Where-Object { $_.DriveType -eq 3 }
            foreach ($disk in $disks) {
                $freeSpacePercent = [math]::Round(($disk.FreeSpace / $disk.Size) * 100, 2)
                $freeSpaceGB = [math]::Round($disk.FreeSpace / 1GB, 2)
                $totalSpaceGB = [math]::Round($disk.Size / 1GB, 2)
                
                $diskStatus = if ($freeSpacePercent -gt 20) { "Healthy" } elseif ($freeSpacePercent -gt 10) { "Warning" } else { "Unhealthy" }
                Add-HealthCheck -Name "Disk Space $($disk.DeviceID)" -Status $diskStatus -Message "$freeSpacePercent% free ($freeSpaceGB GB / $totalSpaceGB GB)" -Component "System" -Details @{ FreePercent = $freeSpacePercent; FreeGB = $freeSpaceGB; TotalGB = $totalSpaceGB }
            }
            
        } catch {
            Add-HealthCheck -Name "System Resources" -Status "Unhealthy" -Message "Failed to retrieve system information: $($_.Exception.Message)" -Component "System"
        }
    }
}

# Network Connectivity Checks
function Test-NetworkConnectivity {
    if ($Component -eq "all" -or $Component -eq "network") {
        Write-Output "Checking network connectivity..."
        
        # Test external connectivity
        $testHosts = @(
            @{ Name = "Google DNS"; Host = "8.8.8.8"; Port = 53 },
            @{ Name = "CloudFlare DNS"; Host = "1.1.1.1"; Port = 53 },
            @{ Name = "Microsoft"; Host = "microsoft.com"; Port = 443 }
        )
        
        foreach ($test in $testHosts) {
            try {
                $result = Test-NetConnection -ComputerName $test.Host -Port $test.Port -InformationLevel Quiet -WarningAction SilentlyContinue
                $status = if ($result) { "Healthy" } else { "Unhealthy" }
                $message = if ($result) { "Connection successful" } else { "Connection failed" }
                Add-HealthCheck -Name "Network - $($test.Name)" -Status $status -Message $message -Component "Network" -Details @{ Host = $test.Host; Port = $test.Port }
            } catch {
                Add-HealthCheck -Name "Network - $($test.Name)" -Status "Unhealthy" -Message "Connection test failed: $($_.Exception.Message)" -Component "Network"
            }
        }
    }
}

# PowerShell Environment Checks
function Test-PowerShellEnvironment {
    if ($Component -eq "all" -or $Component -eq "powershell") {
        Write-Output "Checking PowerShell environment..."
        
        # PowerShell Version
        $psVersion = $PSVersionTable.PSVersion
        $psStatus = if ($psVersion.Major -ge 5) { "Healthy" } else { "Warning" }
        Add-HealthCheck -Name "PowerShell Version" -Status $psStatus -Message "Version $psVersion" -Component "PowerShell" -Details @{ Version = $psVersion.ToString() }
        
        # Execution Policy
        try {
            $execPolicy = Get-ExecutionPolicy
            $policyStatus = if ($execPolicy -in @("RemoteSigned", "Unrestricted", "Bypass")) { "Healthy" } else { "Warning" }
            Add-HealthCheck -Name "Execution Policy" -Status $policyStatus -Message "Policy: $execPolicy" -Component "PowerShell" -Details @{ Policy = $execPolicy }
        } catch {
            Add-HealthCheck -Name "Execution Policy" -Status "Warning" -Message "Could not determine execution policy" -Component "PowerShell"
        }
        
        # Test script execution
        try {
            $testResult = Invoke-Expression "Get-Date"
            Add-HealthCheck -Name "Script Execution" -Status "Healthy" -Message "PowerShell commands execute successfully" -Component "PowerShell"
        } catch {
            Add-HealthCheck -Name "Script Execution" -Status "Unhealthy" -Message "PowerShell execution failed: $($_.Exception.Message)" -Component "PowerShell"
        }
    }
}

# DNS Resolution Checks
function Test-DnsResolution {
    if ($Component -eq "all" -or $Component -eq "dns") {
        Write-Output "Checking DNS resolution..."
        
        $testDomains = @("google.com", "microsoft.com", "github.com")
        
        foreach ($domain in $testDomains) {
            try {
                $dnsResult = Resolve-DnsName -Name $domain -Type A -ErrorAction Stop
                Add-HealthCheck -Name "DNS Resolution - $domain" -Status "Healthy" -Message "Resolved to $($dnsResult[0].IPAddress)" -Component "DNS" -Details @{ Domain = $domain; IP = $dnsResult[0].IPAddress }
            } catch {
                Add-HealthCheck -Name "DNS Resolution - $domain" -Status "Unhealthy" -Message "DNS resolution failed: $($_.Exception.Message)" -Component "DNS"
            }
        }
    }
}

# Service Dependency Checks
function Test-ServiceDependencies {
    if ($Component -eq "all" -or $Component -eq "services") {
        Write-Output "Checking service dependencies..."
        
        # Check critical Windows services
        $criticalServices = @("Winmgmt", "Dnscache", "EventLog", "W32Time")
        
        foreach ($serviceName in $criticalServices) {
            try {
                $service = Get-Service -Name $serviceName -ErrorAction Stop
                $status = if ($service.Status -eq "Running") { "Healthy" } else { "Unhealthy" }
                $message = "Status: $($service.Status)"
                Add-HealthCheck -Name "Service - $serviceName" -Status $status -Message $message -Component "Services" -Details @{ ServiceName = $serviceName; Status = $service.Status.ToString() }
            } catch {
                Add-HealthCheck -Name "Service - $serviceName" -Status "Unhealthy" -Message "Service not found or inaccessible" -Component "Services"
            }
        }
        
        # Check if IIS is available (for webhook hosting)
        try {
            $iisService = Get-Service -Name "W3SVC" -ErrorAction SilentlyContinue
            if ($iisService) {
                $status = if ($iisService.Status -eq "Running") { "Healthy" } else { "Warning" }
                Add-HealthCheck -Name "Web Server (IIS)" -Status $status -Message "IIS Status: $($iisService.Status)" -Component "Services"
            } else {
                Add-HealthCheck -Name "Web Server (IIS)" -Status "Warning" -Message "IIS not detected (may be using Kestrel)" -Component "Services"
            }
        } catch {
            Add-HealthCheck -Name "Web Server (IIS)" -Status "Warning" -Message "Could not check IIS status" -Component "Services"
        }
    }
}

# Application-Specific Checks
function Test-ApplicationHealth {
    if ($Component -eq "all" -or $Component -eq "app") {
        Write-Output "Checking application health..."
        
        # Check if webhook API process is running
        try {
            $dotnetProcesses = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue
            if ($dotnetProcesses) {
                Add-HealthCheck -Name "Application Process" -Status "Healthy" -Message "$($dotnetProcesses.Count) .NET process(es) running" -Component "Application" -Details @{ ProcessCount = $dotnetProcesses.Count }
            } else {
                Add-HealthCheck -Name "Application Process" -Status "Warning" -Message "No .NET processes detected" -Component "Application"
            }
        } catch {
            Add-HealthCheck -Name "Application Process" -Status "Warning" -Message "Could not check application processes" -Component "Application"
        }
        
        # Check webhook scripts directory
        $scriptsPath = "./scripts/powershell"
        if (Test-Path $scriptsPath) {
            $scriptCount = (Get-ChildItem -Path $scriptsPath -Filter "*.ps1").Count
            Add-HealthCheck -Name "Scripts Directory" -Status "Healthy" -Message "$scriptCount PowerShell scripts available" -Component "Application" -Details @{ ScriptsPath = $scriptsPath; ScriptCount = $scriptCount }
        } else {
            Add-HealthCheck -Name "Scripts Directory" -Status "Warning" -Message "Scripts directory not found at $scriptsPath" -Component "Application"
        }
        
        # Check temp directory for logs
        $tempPath = "C:\temp"
        if (Test-Path $tempPath) {
            try {
                $testFile = Join-Path $tempPath "health-check-test.tmp"
                "test" | Out-File -FilePath $testFile -Force
                Remove-Item -Path $testFile -Force
                Add-HealthCheck -Name "Temp Directory Access" -Status "Healthy" -Message "Read/write access confirmed" -Component "Application"
            } catch {
                Add-HealthCheck -Name "Temp Directory Access" -Status "Warning" -Message "Limited access to temp directory" -Component "Application"
            }
        } else {
            Add-HealthCheck -Name "Temp Directory Access" -Status "Warning" -Message "Temp directory not found" -Component "Application"
        }
    }
}

# Execute all health checks
Write-Output "Starting health check at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Output "Component filter: $Component"

Test-SystemResources
Test-NetworkConnectivity
Test-PowerShellEnvironment
Test-DnsResolution
Test-ServiceDependencies
Test-ApplicationHealth

# Generate output based on format
Write-Output "`nHealth Check Summary:"
Write-Output "===================="
Write-Output "Overall Status: $($healthResults.OverallStatus)"
Write-Output "Total Checks: $($healthResults.Summary.Total)"
Write-Output "Passed: $($healthResults.Summary.Passed)"
Write-Output "Warnings: $($healthResults.Summary.Warnings)"
Write-Output "Failed: $($healthResults.Summary.Failed)"

if ($OutputFormat -eq "json") {
    Write-Output "`nJSON Output:"
    $healthResults | ConvertTo-Json -Depth 4
} elseif ($OutputFormat -eq "prometheus") {
    Write-Output "`nPrometheus Metrics:"
    Write-Output "# HELP webhook_health_check_total Total number of health checks performed"
    Write-Output "# TYPE webhook_health_check_total counter"
    Write-Output "webhook_health_check_total $($healthResults.Summary.Total)"
    
    Write-Output "# HELP webhook_health_check_passed Number of health checks passed"
    Write-Output "# TYPE webhook_health_check_passed gauge"
    Write-Output "webhook_health_check_passed $($healthResults.Summary.Passed)"
    
    Write-Output "# HELP webhook_health_check_failed Number of health checks failed"
    Write-Output "# TYPE webhook_health_check_failed gauge"
    Write-Output "webhook_health_check_failed $($healthResults.Summary.Failed)"
    
    Write-Output "# HELP webhook_health_status Overall health status (1=healthy, 0=unhealthy)"
    Write-Output "# TYPE webhook_health_status gauge"
    $healthStatusValue = if ($healthResults.OverallStatus -eq "Healthy") { 1 } else { 0 }
    Write-Output "webhook_health_status $healthStatusValue"
}

if ($Detailed) {
    Write-Output "`nDetailed Results:"
    $healthResults.Checks | Format-Table -Property Name, Component, Status, Message -AutoSize
}

# Exit with appropriate code
if ($healthResults.OverallStatus -eq "Healthy") {
    Write-Output "`nHealth check completed successfully."
    exit 0
} else {
    Write-Output "`nHealth check detected issues."
    exit 1
}
