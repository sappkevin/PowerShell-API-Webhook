param(
    [int]$RetentionDays = 30,
    [string]$LogLevel = "Info",
    [switch]$DryRun,
    [switch]$Detailed,
    [string[]]$IncludePaths = @(),
    [string[]]$ExcludePaths = @(),
    [string]$OutputFormat = "text"
)

<#
.SYNOPSIS
Daily cleanup script for webhook API maintenance

.DESCRIPTION
Performs comprehensive cleanup operations including:
- Log file cleanup based on retention policy
- Temporary file cleanup
- Old webhook execution logs
- Hangfire database cleanup
- System temp files
- Application cache cleanup
- Performance metrics cleanup

.PARAMETER RetentionDays
Number of days to retain files (default: 30)

.PARAMETER LogLevel
Logging level: Debug, Info, Warning, Error (default: Info)

.PARAMETER DryRun
Simulate cleanup operations without actually deleting files

.PARAMETER Detailed
Show detailed information about each cleanup operation

.PARAMETER IncludePaths
Additional paths to include in cleanup

.PARAMETER ExcludePaths
Paths to exclude from cleanup

.PARAMETER OutputFormat
Output format: text, json, csv (default: text)

.EXAMPLE
.\daily-cleanup.ps1 -RetentionDays 30 -DryRun

.EXAMPLE
.\daily-cleanup.ps1 -RetentionDays 7 -LogLevel Debug -Detailed

.EXAMPLE
.\daily-cleanup.ps1 -RetentionDays 60 -IncludePaths @("C:\CustomLogs") -OutputFormat json
#>

# Initialize cleanup results
$cleanupResults = @{
    StartTime = Get-Date
    EndTime = $null
    RetentionDays = $RetentionDays
    DryRun = $DryRun.IsPresent
    Operations = @()
    Summary = @{
        TotalOperations = 0
        SuccessfulOperations = 0
        FailedOperations = 0
        FilesProcessed = 0
        FilesDeleted = 0
        SpaceFreedMB = 0
        Errors = @()
    }
}

# Function to write log messages
function Write-CleanupLog {
    param(
        [string]$Message,
        [ValidateSet('Debug', 'Info', 'Warning', 'Error')]
        [string]$Level = 'Info',
        [string]$Operation = '',
        [hashtable]$Details = @{}
    )
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[$timestamp] [$Level] $Message"
    
    # Filter based on log level
    $levelPriority = @{ 'Debug' = 0; 'Info' = 1; 'Warning' = 2; 'Error' = 3 }
    $currentLevelPriority = $levelPriority[$LogLevel]
    $messageLevelPriority = $levelPriority[$Level]
    
    if ($messageLevelPriority -ge $currentLevelPriority) {
        switch ($Level) {
            'Error' { Write-Host $logEntry -ForegroundColor Red }
            'Warning' { Write-Host $logEntry -ForegroundColor Yellow }
            'Debug' { Write-Host $logEntry -ForegroundColor Gray }
            default { Write-Host $logEntry }
        }
    }
    
    # Add to results
    $cleanupResults.Operations += @{
        Timestamp = $timestamp
        Level = $Level
        Operation = $Operation
        Message = $Message
        Details = $Details
    }
}

# Function to get file size in MB
function Get-FileSizeMB {
    param([long]$SizeBytes)
    return [math]::Round($SizeBytes / 1MB, 2)
}

# Function to clean up directory with retention policy
function Clear-DirectoryWithRetention {
    param(
        [string]$Path,
        [string]$Description,
        [string]$FilePattern = "*.*",
        [int]$RetentionDays,
        [bool]$IsDryRun = $false
    )
    
    Write-CleanupLog -Message "Starting cleanup: $Description" -Operation "DirectoryCleanup" -Details @{ Path = $Path; Pattern = $FilePattern; Retention = $RetentionDays }
    
    if (-not (Test-Path $Path)) {
        Write-CleanupLog -Message "Path does not exist: $Path" -Level "Warning" -Operation "DirectoryCleanup"
        return
    }
    
    try {
        $cutoffDate = (Get-Date).AddDays(-$RetentionDays)
        $files = Get-ChildItem -Path $Path -Filter $FilePattern -Recurse -File | Where-Object { $_.LastWriteTime -lt $cutoffDate }
        
        if ($files.Count -eq 0) {
            Write-CleanupLog -Message "No files found older than $RetentionDays days in $Description" -Operation "DirectoryCleanup"
            return
        }
        
        $totalSize = ($files | Measure-Object -Property Length -Sum).Sum
        $totalSizeMB = Get-FileSizeMB $totalSize
        
        Write-CleanupLog -Message "Found $($files.Count) files ($totalSizeMB MB) older than $RetentionDays days" -Operation "DirectoryCleanup" -Details @{ FileCount = $files.Count; SizeMB = $totalSizeMB }
        
        $cleanupResults.Summary.FilesProcessed += $files.Count
        
        if ($IsDryRun) {
            Write-CleanupLog -Message "[DRY RUN] Would delete $($files.Count) files ($totalSizeMB MB)" -Level "Info" -Operation "DirectoryCleanup"
        } else {
            $deletedCount = 0
            foreach ($file in $files) {
                try {
                    if ($Detailed) {
                        Write-CleanupLog -Message "Deleting: $($file.FullName)" -Level "Debug" -Operation "FileDelete"
                    }
                    Remove-Item -Path $file.FullName -Force -ErrorAction Stop
                    $deletedCount++
                } catch {
                    Write-CleanupLog -Message "Failed to delete $($file.FullName): $($_.Exception.Message)" -Level "Error" -Operation "FileDelete"
                    $cleanupResults.Summary.Errors += "Failed to delete $($file.FullName): $($_.Exception.Message)"
                }
            }
            
            $cleanupResults.Summary.FilesDeleted += $deletedCount
            $cleanupResults.Summary.SpaceFreedMB += $totalSizeMB
            Write-CleanupLog -Message "Successfully deleted $deletedCount/$($files.Count) files ($totalSizeMB MB freed)" -Operation "DirectoryCleanup"
        }
        
        $cleanupResults.Summary.SuccessfulOperations++
    } catch {
        Write-CleanupLog -Message "Error cleaning $Description`: $($_.Exception.Message)" -Level "Error" -Operation "DirectoryCleanup"
        $cleanupResults.Summary.FailedOperations++
        $cleanupResults.Summary.Errors += "Error cleaning $Description`: $($_.Exception.Message)"
    }
    
    $cleanupResults.Summary.TotalOperations++
}

# Function to cleanup empty directories
function Remove-EmptyDirectories {
    param(
        [string]$Path,
        [bool]$IsDryRun = $false
    )
    
    if (-not (Test-Path $Path)) {
        return
    }
    
    try {
        $emptyDirs = Get-ChildItem -Path $Path -Recurse -Directory | Where-Object { 
            (Get-ChildItem -Path $_.FullName -Force | Measure-Object).Count -eq 0 
        }
        
        if ($emptyDirs.Count -gt 0) {
            Write-CleanupLog -Message "Found $($emptyDirs.Count) empty directories" -Operation "EmptyDirectoryCleanup"
            
            if ($IsDryRun) {
                Write-CleanupLog -Message "[DRY RUN] Would remove $($emptyDirs.Count) empty directories" -Operation "EmptyDirectoryCleanup"
            } else {
                foreach ($dir in $emptyDirs) {
                    try {
                        Remove-Item -Path $dir.FullName -Force -ErrorAction Stop
                        if ($Detailed) {
                            Write-CleanupLog -Message "Removed empty directory: $($dir.FullName)" -Level "Debug" -Operation "DirectoryDelete"
                        }
                    } catch {
                        Write-CleanupLog -Message "Failed to remove directory $($dir.FullName): $($_.Exception.Message)" -Level "Warning" -Operation "DirectoryDelete"
                    }
                }
                Write-CleanupLog -Message "Removed $($emptyDirs.Count) empty directories" -Operation "EmptyDirectoryCleanup"
            }
        }
    } catch {
        Write-CleanupLog -Message "Error removing empty directories: $($_.Exception.Message)" -Level "Error" -Operation "EmptyDirectoryCleanup"
    }
}

# Function to cleanup Hangfire database
function Clear-HangfireData {
    param([bool]$IsDryRun = $false)
    
    Write-CleanupLog -Message "Checking Hangfire data cleanup" -Operation "HangfireCleanup"
    
    $hangfireDbPath = "Data/hangfire.db"
    if (Test-Path $hangfireDbPath) {
        try {
            $dbSize = Get-FileSizeMB (Get-Item $hangfireDbPath).Length
            Write-CleanupLog -Message "Hangfire database size: $dbSize MB" -Operation "HangfireCleanup" -Details @{ SizeMB = $dbSize }
            
            # Note: In a real implementation, you might want to connect to the database
            # and clean up old job history, but for safety we'll just report the size
            if ($dbSize -gt 100) {
                Write-CleanupLog -Message "Hangfire database is large ($dbSize MB). Consider manual cleanup of old job data." -Level "Warning" -Operation "HangfireCleanup"
            }
        } catch {
            Write-CleanupLog -Message "Error checking Hangfire database: $($_.Exception.Message)" -Level "Warning" -Operation "HangfireCleanup"
        }
    } else {
        Write-CleanupLog -Message "Hangfire database not found (may be using SQL Server)" -Operation "HangfireCleanup"
    }
}

# Function to cleanup Windows Event Logs (application-specific)
function Clear-ApplicationEventLogs {
    param([bool]$IsDryRun = $false)
    
    Write-CleanupLog -Message "Checking application event logs" -Operation "EventLogCleanup"
    
    try {
        # Check for webhook-specific event logs
        $logNames = @("Application", "System")
        foreach ($logName in $logNames) {
            $events = Get-WinEvent -LogName $logName -MaxEvents 1000 -ErrorAction SilentlyContinue | 
                      Where-Object { $_.TimeCreated -lt (Get-Date).AddDays(-$RetentionDays) }
            
            if ($events.Count -gt 0) {
                Write-CleanupLog -Message "Found $($events.Count) old events in $logName log (older than $RetentionDays days)" -Operation "EventLogCleanup"
                # Note: We don't actually clear Windows Event Logs as this requires special permissions
                # This is just for monitoring purposes
            }
        }
    } catch {
        Write-CleanupLog -Message "Could not check event logs: $($_.Exception.Message)" -Level "Warning" -Operation "EventLogCleanup"
    }
}

# Main cleanup execution
Write-CleanupLog -Message "Starting daily cleanup routine" -Operation "CleanupStart" -Details @{ 
    RetentionDays = $RetentionDays; 
    DryRun = $DryRun.IsPresent;
    LogLevel = $LogLevel 
}

if ($DryRun) {
    Write-CleanupLog -Message "*** DRY RUN MODE - No files will be deleted ***" -Level "Warning" -Operation "Configuration"
}

# Define cleanup locations
$cleanupLocations = @(
    @{ Path = "C:\temp"; Description = "System temp files"; Pattern = "*.*" },
    @{ Path = "C:\Windows\Temp"; Description = "Windows temp files"; Pattern = "*.*" },
    @{ Path = ".\logs"; Description = "Application logs"; Pattern = "*.log" },
    @{ Path = ".\Data"; Description = "Application data files"; Pattern = "*.tmp" },
    @{ Path = "$env:TEMP"; Description = "User temp files"; Pattern = "*.*" },
    @{ Path = "C:\inetpub\logs\LogFiles"; Description = "IIS log files"; Pattern = "*.log" }
)

# Add include paths
foreach ($includePath in $IncludePaths) {
    $cleanupLocations += @{ Path = $includePath; Description = "Custom path: $includePath"; Pattern = "*.*" }
}

# Execute cleanup for each location
foreach ($location in $cleanupLocations) {
    # Skip excluded paths
    if ($ExcludePaths -contains $location.Path) {
        Write-CleanupLog -Message "Skipping excluded path: $($location.Path)" -Operation "PathSkip"
        continue
    }
    
    Clear-DirectoryWithRetention -Path $location.Path -Description $location.Description -FilePattern $location.Pattern -RetentionDays $RetentionDays -IsDryRun $DryRun
}

# Cleanup empty directories
Write-CleanupLog -Message "Cleaning up empty directories" -Operation "EmptyDirectoryCleanup"
$emptyDirPaths = @("C:\temp", ".\logs", ".\Data")
foreach ($path in $emptyDirPaths) {
    Remove-EmptyDirectories -Path $path -IsDryRun $DryRun
}

# Cleanup Hangfire data
Clear-HangfireData -IsDryRun $DryRun

# Check application event logs
Clear-ApplicationEventLogs -IsDryRun $DryRun

# Cleanup webhook-specific files
Write-CleanupLog -Message "Cleaning webhook-specific files" -Operation "WebhookCleanup"
$webhookPaths = @(
    @{ Path = "C:\temp\condForwarderAPI.log"; Description = "DNS forwarder API logs"; Pattern = "condForwarderAPI*.log" },
    @{ Path = "C:\temp\dnsforwarder_metrics.txt"; Description = "DNS forwarder metrics"; Pattern = "dnsforwarder_metrics*.txt" },
    @{ Path = ".\performance_tests\reports"; Description = "Performance test reports"; Pattern = "*.json" }
)

foreach ($webhookPath in $webhookPaths) {
    if (Test-Path (Split-Path $webhookPath.Path -Parent)) {
        Clear-DirectoryWithRetention -Path (Split-Path $webhookPath.Path -Parent) -Description $webhookPath.Description -FilePattern $webhookPath.Pattern -RetentionDays $RetentionDays -IsDryRun $DryRun
    }
}

# Cleanup old PowerShell transcripts if any exist
Write-CleanupLog -Message "Cleaning PowerShell transcripts" -Operation "TranscriptCleanup"
$transcriptPaths = @(
    "$env:USERPROFILE\Documents\PowerShell_transcript*",
    "$env:USERPROFILE\Documents\20*.txt"
)

foreach ($transcriptPath in $transcriptPaths) {
    $transcriptDir = Split-Path $transcriptPath -Parent
    $transcriptPattern = Split-Path $transcriptPath -Leaf
    if (Test-Path $transcriptDir) {
        Clear-DirectoryWithRetention -Path $transcriptDir -Description "PowerShell transcripts" -FilePattern $transcriptPattern -RetentionDays $RetentionDays -IsDryRun $DryRun
    }
}

# System registry cleanup (safe operations only)
function Clear-SystemCaches {
    param([bool]$IsDryRun = $false)
    
    Write-CleanupLog -Message "Cleaning system caches" -Operation "SystemCacheCleanup"
    
    try {
        # Clear DNS cache
        if (-not $IsDryRun) {
            Clear-DnsClientCache
            Write-CleanupLog -Message "DNS cache cleared" -Operation "SystemCacheCleanup"
        } else {
            Write-CleanupLog -Message "[DRY RUN] Would clear DNS cache" -Operation "SystemCacheCleanup"
        }
        
        # Check Windows Update cache
        $wuCachePath = "C:\Windows\SoftwareDistribution\Download"
        if (Test-Path $wuCachePath) {
            $wuCacheSize = Get-FileSizeMB ((Get-ChildItem $wuCachePath -Recurse -File | Measure-Object -Property Length -Sum).Sum)
            if ($wuCacheSize -gt 1000) {  # Only report if > 1GB
                Write-CleanupLog -Message "Windows Update cache is large: $wuCacheSize MB. Consider manual cleanup." -Level "Warning" -Operation "SystemCacheCleanup"
            }
        }
        
    } catch {
        Write-CleanupLog -Message "Error during system cache cleanup: $($_.Exception.Message)" -Level "Warning" -Operation "SystemCacheCleanup"
    }
}

# Execute system cache cleanup
Clear-SystemCaches -IsDryRun $DryRun

# Final summary and reporting
$cleanupResults.EndTime = Get-Date
$duration = $cleanupResults.EndTime - $cleanupResults.StartTime

Write-CleanupLog -Message "Cleanup completed" -Operation "CleanupEnd" -Details @{
    Duration = $duration.ToString()
    TotalOperations = $cleanupResults.Summary.TotalOperations
    SuccessfulOperations = $cleanupResults.Summary.SuccessfulOperations
    FailedOperations = $cleanupResults.Summary.FailedOperations
    FilesDeleted = $cleanupResults.Summary.FilesDeleted
    SpaceFreedMB = $cleanupResults.Summary.SpaceFreedMB
}

# Generate final report
Write-Output ""
Write-Output "=================================="
Write-Output "DAILY CLEANUP SUMMARY REPORT"
Write-Output "=================================="
Write-Output "Execution Time: $($cleanupResults.StartTime.ToString('yyyy-MM-dd HH:mm:ss')) - $($cleanupResults.EndTime.ToString('yyyy-MM-dd HH:mm:ss'))"
Write-Output "Duration: $($duration.Hours)h $($duration.Minutes)m $($duration.Seconds)s"
Write-Output "Retention Policy: $RetentionDays days"
Write-Output "Mode: $(if ($DryRun) { 'DRY RUN' } else { 'LIVE EXECUTION' })"
Write-Output ""
Write-Output "RESULTS:"
Write-Output "--------"
Write-Output "Total Operations: $($cleanupResults.Summary.TotalOperations)"
Write-Output "Successful: $($cleanupResults.Summary.SuccessfulOperations)"
Write-Output "Failed: $($cleanupResults.Summary.FailedOperations)"
Write-Output "Files Processed: $($cleanupResults.Summary.FilesProcessed)"
Write-Output "Files Deleted: $($cleanupResults.Summary.FilesDeleted)"
Write-Output "Space Freed: $($cleanupResults.Summary.SpaceFreedMB) MB"

if ($cleanupResults.Summary.Errors.Count -gt 0) {
    Write-Output ""
    Write-Output "ERRORS ENCOUNTERED:"
    Write-Output "-------------------"
    foreach ($error in $cleanupResults.Summary.Errors) {
        Write-Output "• $error"
    }
}

# Output in requested format
if ($OutputFormat -eq "json") {
    Write-Output ""
    Write-Output "JSON OUTPUT:"
    Write-Output "============"
    $cleanupResults | ConvertTo-Json -Depth 4
} elseif ($OutputFormat -eq "csv") {
    Write-Output ""
    Write-Output "CSV OUTPUT:"
    Write-Output "==========="
    $csvData = $cleanupResults.Operations | Select-Object Timestamp, Level, Operation, Message
    $csvData | ConvertTo-Csv -NoTypeInformation
}

# Generate performance metrics for monitoring
$metricsOutput = @"
# HELP webhook_cleanup_duration_seconds Time taken for cleanup operation
# TYPE webhook_cleanup_duration_seconds gauge
webhook_cleanup_duration_seconds $($duration.TotalSeconds)

# HELP webhook_cleanup_files_deleted_total Total files deleted during cleanup
# TYPE webhook_cleanup_files_deleted_total counter
webhook_cleanup_files_deleted_total $($cleanupResults.Summary.FilesDeleted)

# HELP webhook_cleanup_space_freed_mb Total space freed in megabytes
# TYPE webhook_cleanup_space_freed_mb gauge
webhook_cleanup_space_freed_mb $($cleanupResults.Summary.SpaceFreedMB)

# HELP webhook_cleanup_operations_total Total cleanup operations performed
# TYPE webhook_cleanup_operations_total counter
webhook_cleanup_operations_total $($cleanupResults.Summary.TotalOperations)

# HELP webhook_cleanup_operations_failed Total cleanup operations that failed
# TYPE webhook_cleanup_operations_failed counter
webhook_cleanup_operations_failed $($cleanupResults.Summary.FailedOperations)

# HELP webhook_cleanup_last_run_timestamp Unix timestamp of last cleanup run
# TYPE webhook_cleanup_last_run_timestamp gauge
webhook_cleanup_last_run_timestamp $([int64](Get-Date -UFormat %s))
"@

# Save metrics to file for Prometheus scraping
try {
    $metricsPath = "C:\temp\webhook_cleanup_metrics.txt"
    $metricsOutput | Out-File -FilePath $metricsPath -Encoding utf8 -Force
    Write-CleanupLog -Message "Metrics saved to $metricsPath" -Operation "MetricsExport"
} catch {
    Write-CleanupLog -Message "Failed to save metrics: $($_.Exception.Message)" -Level "Warning" -Operation "MetricsExport"
}

# Final status and exit code
if ($cleanupResults.Summary.FailedOperations -eq 0) {
    Write-Output ""
    Write-Output "✅ Cleanup completed successfully!"
    exit 0
} elseif ($cleanupResults.Summary.FailedOperations -lt $cleanupResults.Summary.SuccessfulOperations) {
    Write-Output ""
    Write-Output "⚠️  Cleanup completed with some warnings/errors."
    exit 1
} else {
    Write-Output ""
    Write-Output "❌ Cleanup completed with significant errors."
    exit 2
}
