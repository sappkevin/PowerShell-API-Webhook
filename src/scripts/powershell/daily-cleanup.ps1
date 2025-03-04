param(
    [int]$RetentionDays = 30
)

$currentDate = Get-Date
$cutoffDate = $currentDate.AddDays(-$RetentionDays)

# Output basic information
Write-Output "Running daily cleanup script at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Output "Retention policy: $RetentionDays days (removing items older than $($cutoffDate.ToString('yyyy-MM-dd')))"

# This is a sample script that would clean up logs, temporary files, etc.
# In a real environment, you'd implement your actual cleanup logic here

# For example, find and remove log files older than retention period
$logDirectory = "./logs"
if (Test-Path $logDirectory) {
    Write-Output "Scanning log directory: $logDirectory"
    
    $oldFiles = Get-ChildItem -Path $logDirectory -File -Filter "*.log" | 
                Where-Object { $_.LastWriteTime -lt $cutoffDate }
    
    if ($oldFiles.Count -gt 0) {
        Write-Output "Found $($oldFiles.Count) log files older than $RetentionDays days"
        foreach ($file in $oldFiles) {
            Write-Output "Removing: $($file.FullName)"
            # In a real script, you'd uncomment the next line to actually remove files
            # Remove-Item -Path $file.FullName -Force
        }
    } else {
        Write-Output "No log files found that exceed the retention period"
    }
} else {
    Write-Output "Log directory not found: $logDirectory"
}

# Similar logic for temp files
$tempDirectory = "./temp"
if (Test-Path $tempDirectory) {
    Write-Output "Scanning temp directory: $tempDirectory"
    
    $oldTempFiles = Get-ChildItem -Path $tempDirectory -File -Recurse | 
                    Where-Object { $_.LastWriteTime -lt $cutoffDate }
    
    if ($oldTempFiles.Count -gt 0) {
        Write-Output "Found $($oldTempFiles.Count) temp files older than $RetentionDays days"
        # In a real script, you'd uncomment the next line to actually remove files
        # $oldTempFiles | Remove-Item -Force
    } else {
        Write-Output "No temp files found that exceed the retention period"
    }
} else {
    Write-Output "Temp directory not found: $tempDirectory"
}

# Example of looking for Hangfire expired records (simulation only)
Write-Output "Simulating cleanup of Hangfire expired records..."
Write-Output "In a production environment, this would connect to the database and clean expired records"

Write-Output "Cleanup script completed successfully"