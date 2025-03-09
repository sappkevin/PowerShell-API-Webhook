param (
    [string]$ApiUrl = "http://localhost:8080",  # Base API URL without endpoint
    [string]$ApiKey = "your-api-key",           # Replace with actual key from appsettings.json
    [int]$ConcurrentUsers = 50,
    [int]$Duration = 2                          # Duration in minutes
)

# Ensure reports directory exists
$reportPath = "performance_tests\reports"
$null = New-Item -ItemType Directory -Path $reportPath -Force

class WebhookLoadTester {
    [string]$apiUrl
    [string]$apiKey
    [int]$concurrentUsers
    [int]$durationSeconds
    [hashtable]$results

    WebhookLoadTester([string]$apiUrl, [string]$apiKey, [int]$concurrentUsers, [int]$durationMinutes) {
        $this.apiUrl = $apiUrl.TrimEnd('/')
        $this.apiKey = $apiKey
        $this.concurrentUsers = $concurrentUsers
        $this.durationSeconds = $durationMinutes * 60
        $this.results = @{
            "GET API" = @{ "success" = 0; "fail" = 0; "latencies" = [System.Collections.Generic.List[double]]::new() }
            "POST API" = @{ "success" = 0; "fail" = 0; "latencies" = [System.Collections.Generic.List[double]]::new() }
        }
    }

    [void] RunTest() {
        Write-Host "Starting load test against $($this.apiUrl)" -ForegroundColor Green
        Write-Host "Concurrent users: $($this.concurrentUsers)"
        Write-Host "Test duration: $($this.durationSeconds / 60) minutes"

        $this.RunScenarios()
        $this.GenerateReport()
    }

    [void] RunScenarios() {
        $startTime = Get-Date
        $payload = @{
            "script" = "health-check-script.ps1"
            "key" = $this.apiKey
        }

        # Run GET and POST scenarios concurrently
        $getJob = Start-Job -ScriptBlock {
            param($tester, $startTime, $payload)
            $tester.RunGetScenario($startTime, $payload)
        } -ArgumentList $this, $startTime, $payload

        $postJob = Start-Job -ScriptBlock {
            param($tester, $startTime, $payload)
            $tester.RunPostScenario($startTime, $payload)
        } -ArgumentList $this, $startTime, $payload

        # Wait for jobs to complete
        Wait-Job -Job $getJob, $postJob | Out-Null
        Remove-Job -Job $getJob, $postJob
    }

    [void] RunGetScenario([datetime]$startTime, [hashtable]$payload) {
        $queryParams = ($payload.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "&"
        $endpoint = "$($this.apiUrl)/webhook/v1?$queryParams"

        while (((Get-Date) - $startTime).TotalSeconds -lt $this.durationSeconds) {
            $jobs = 1..([math]::Ceiling($this.concurrentUsers / 2)) | ForEach-Object {
                Start-Job -ScriptBlock {
                    param($tester, $url)
                    $tester.SendGetRequest($url)
                } -ArgumentList $this, $endpoint
            }
            $jobs | Wait-Job | ForEach-Object { Receive-Job -Job $_ -ErrorAction SilentlyContinue; Remove-Job -Job $_ }
            Start-Sleep -Milliseconds 100  # 10 RPS
            $elapsed = [int](((Get-Date) - $startTime).TotalSeconds)
            Write-Progress -Activity "GET API Requests" -Status "$elapsed/$($this.durationSeconds)s" -PercentComplete ($elapsed / $this.durationSeconds * 100)
        }
    }

    [void] RunPostScenario([datetime]$startTime, [hashtable]$payload) {
        $endpoint = "$($this.apiUrl)/webhook/v1"

        while (((Get-Date) - $startTime).TotalSeconds -lt $this.durationSeconds) {
            $jobs = 1..([math]::Ceiling($this.concurrentUsers / 2)) | ForEach-Object {
                Start-Job -ScriptBlock {
                    param($tester, $url, $payload)
                    $tester.SendPostRequest($url, $payload, "POST API")
                } -ArgumentList $this, $endpoint, $payload
            }
            $jobs | Wait-Job | ForEach-Object { Receive-Job -Job $_ -ErrorAction SilentlyContinue; Remove-Job -Job $_ }
            Start-Sleep -Milliseconds 100  # 10 RPS
            $elapsed = [int](((Get-Date) - $startTime).TotalSeconds)
            Write-Progress -Activity "POST API Requests" -Status "$elapsed/$($this.durationSeconds)s" -PercentComplete ($elapsed / $this.durationSeconds * 100)
        }
    }

    [void] SendGetRequest([string]$url) {
        $startTime = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $response = Invoke-WebRequest -Uri $url -Method Get -Headers @{"Accept" = "application/json"} -UseBasicParsing
            if ($response.StatusCode -lt 400) {
                $latency = $startTime.Elapsed.TotalSeconds
                [Threading.Interlocked]::Increment([ref]$this.results["GET API"].success) | Out-Null
                $this.results["GET API"].latencies.Add($latency)
            } else {
                Write-Host "GET failed: $($response.StatusCode)" -ForegroundColor Red
                [Threading.Interlocked]::Increment([ref]$this.results["GET API"].fail) | Out-Null
            }
        } catch {
            Write-Host "GET request error: $_" -ForegroundColor Red
            [Threading.Interlocked]::Increment([ref]$this.results["GET API"].fail) | Out-Null
        }
    }

    [void] SendPostRequest([string]$url, [hashtable]$payload, [string]$scenario) {
        $startTime = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $response = Invoke-WebRequest -Uri $url -Method Post -Body ($payload | ConvertTo-Json) -ContentType "application/json" -Headers @{"Accept" = "application/json"} -UseBasicParsing
            if ($response.StatusCode -lt 400) {
                $latency = $startTime.Elapsed.TotalSeconds
                [Threading.Interlocked]::Increment([ref]$this.results[$scenario].success) | Out-Null
                $this.results[$scenario].latencies.Add($latency)
            } else {
                Write-Host "$scenario failed: $($response.StatusCode)" -ForegroundColor Red
                [Threading.Interlocked]::Increment([ref]$this.results[$scenario].fail) | Out-Null
            }
        } catch {
            Write-Host "$scenario request error: $_" -ForegroundColor Red
            [Threading.Interlocked]::Increment([ref]$this.results[$scenario].fail) | Out-Null
        }
    }

    [void] GenerateReport() {
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        if(!$reportPath){$reportPath = "performance_tests\reports"}
        $reportFile = "$reportPath\load_test_results_$timestamp.json"

        Write-Host "`nWebhookShell API Load Test Results" -ForegroundColor Cyan
        Write-Host ("-" * 80)
        Write-Host ("{0,-15} {1,8} {2,8} {3,8} {4,8} {5,8}" -f "Scenario", "Success", "Fail", "RPS", "Avg(ms)", "p95(ms)")

        foreach ($scenario in $this.results.Keys) {
            $data = $this.results[$scenario]
            if ($data.latencies.Count -gt 0) {
                $latenciesMs = $data.latencies | ForEach-Object { $_ * 1000 }
                $rps = ($data.success + $data.fail) / $this.durationSeconds
                $avgLatency = ($latenciesMs | Measure-Object -Average).Average
                $sortedLatencies = $latenciesMs | Sort-Object
                $p95Latency = $sortedLatencies[[math]::Floor($sortedLatencies.Count * 0.95)]

                Write-Host ("{0,-15} {1,8} {2,8} {3,8:F2} {4,8:F2} {5,8:F2}" -f $scenario, $data.success, $data.fail, $rps, $avgLatency, $p95Latency)
            } else {
                Write-Host ("{0,-15} {1,8} {2,8} {3,8} {4,8} {5,8}" -f $scenario, 0, 0, 0, "N/A", "N/A")
            }
        }

        $reportData = @{
            test_info = @{
                api_url = $this.apiUrl
                concurrent_users = $this.concurrentUsers
                duration_seconds = $this.durationSeconds
                timestamp = $timestamp
            }
            results = $this.results
        }

        $reportData | ConvertTo-Json -Depth 10 | Out-File $reportFile
        Write-Host "Results saved to $reportFile" -ForegroundColor Green
    }
}

# Main execution
$tester = [WebhookLoadTester]::new($ApiUrl, $ApiKey, $ConcurrentUsers, $Duration)
$tester.RunTest()
