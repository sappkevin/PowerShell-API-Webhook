Write-Output @{
    Status = "Healthy"
    Timestamp = (Get-Date).ToString("o")
    Version = "1.0"
} | ConvertTo-Json
