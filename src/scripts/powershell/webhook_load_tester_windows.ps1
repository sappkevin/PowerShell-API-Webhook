param (
    [string]$ApiUrl = "http://localhost:80",
    [string]$ApiKey = "24ffc5be-7dd8-479f-898e-27169bf23e7f",
    [int]$ConcurrentUsers = 50,
    [int]$Duration = 5  # Duration in minutes
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
        $this.apiUrl = $apiUrl
        $this.apiKey = $apiKey
        $this.concurrentUsers = $concurrentUsers
        $this.durationSeconds = $durationMinutes * 60
        $this.results = @{
            "GET API" = @{ "success" = 0; "fail" = 0; "latencies" = [System.Collections.Generic.List[double]]::new() }
            "POST API" = @{ "success" = 0; "fail" = 0; "latencies" = [System.Collections.Generic.List[double]]::new() }
            "Background Job" = @{ "success" = 0; "fail" = 0; "latencies" = [System.Collections.Generic.List[double]]::new() }
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

        # Run scenarios as background jobs
        $jobs = @()
        $jobs += Start-Job -ScriptBlock { param($tester, $start, $payload) $tester.RunGetScenario($start, $payload) } -ArgumentList $this, $startTime, $payload
        $jobs += Start-Job -ScriptBlock { param($tester, $start, $payload) $tester.RunPostScenario($start, $payload) } -ArgumentList $this, $startTime, $payload
        $jobs += Start-Job -ScriptBlock { param($tester, $start, $payload) $tester.RunBackgroundJobScenario($start, $payload) } -ArgumentList $this, $startTime, $payload

        # Wait for all jobs to complete
        $jobs | Wait-Job | Receive-Job
        $jobs | Remove-Job
    }

    [void] RunGetScenario([datetime]$startTime, [hashtable]$payload) {
        $queryParams = ($payload.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "&"
        $endpoint = "$($this.apiUrl)/webhook/v1?$queryParams"

        while (((Get-Date) - $startTime).TotalSeconds -lt $this.durationSeconds) {
            1..($this.concurrentUsers / 3) | ForEach-Object -ThrottleLimit ($this.concurrentUsers / 3) -Parallel {
                $tester = $using:this
                $tester.SendGetRequest($using:endpoint)
            }
            Start-Sleep -Milliseconds 100  # 10 requests per second
            Write-Progress -Activity "GET API Requests" -Status "$([int](((Get-Date) - $startTime).TotalSeconds))s elapsed" -PercentComplete (((Get-Date) - $startTime).TotalSeconds / $this.durationSeconds * 100)
        }
    }

    [void] RunPostScenario([datetime]$startTime, [hashtable]$payload) {
        $endpoint = "$($this.apiUrl)/webhook/v1"

        while (((Get-Date) - $startTime).TotalSeconds -lt $this.durationSeconds) {
            1..($this.concurrentUsers / 3) | ForEach-Object -ThrottleLimit ($this.concurrentUsers / 3) -Parallel {
                $tester = $using:this
                $tester.SendPostRequest($using:endpoint, $using:payload, "POST API")
            }
            Start-Sleep -Milliseconds 100  # 10 requests per second
            Write-Progress -Activity "POST API Requests" -Status "$([int](((Get-Date) - $startTime).TotalSeconds))s elapsed" -PercentComplete (((Get-Date) - $startTime).TotalSeconds / $this.durationSeconds * 100)
        }
    }

    [void] RunBackgroundJobScenario([datetime]$startTime, [hashtable]$payload) {
        $endpoint = "$($this.apiUrl)/jobs/v1/enqueue"

        while (((Get-Date) - $startTime).TotalSeconds -lt $this.durationSeconds) {
            1..($this.concurrentUsers / 3) | ForEach-Object -ThrottleLimit ($this.concurrentUsers / 3) -Parallel {
                $tester = $using:this
                $tester.SendPostRequest($using:endpoint, $using:payload, "Background Job")
            }
            Start-Sleep -Milliseconds 67  # ~15 requests per second
            Write-Progress -Activity "Background Job Requests" -Status "$([int](((Get-Date) - $startTime).TotalSeconds))s elapsed" -PercentComplete (((Get-Date) - $startTime).TotalSeconds / $this.durationSeconds * 100)
        }
    }

    [void] SendGetRequest([string]$url) {
        $startTime = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $response = Invoke-WebRequest -Uri $url -Method Get -Headers @{"Accept" = "application/json"} -UseBasicParsing
            $latency = $startTime.Elapsed.TotalSeconds
            $this.results["GET API"].latencies.Add($latency)
            if ($response.StatusCode -lt 400) {
                $this.results["GET API"].success++
            } else {
                $this.results["GET API"].fail++
            }
        } catch {
            Write-Host "GET request error: $_" -ForegroundColor Red
            $this.results["GET API"].fail++
        }
    }

    [void] SendPostRequest([string]$url, [hashtable]$payload, [string]$scenario) {
        $startTime = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $response = Invoke-WebRequest -Uri $url -Method Post -Body ($payload | ConvertTo-Json) -ContentType "application/json" -Headers @{"Accept" = "application/json"} -UseBasicParsing
            $latency = $startTime.Elapsed.TotalSeconds
            $this.results[$scenario].latencies.Add($latency)
            if ($response.StatusCode -lt 400) {
                $this.results[$scenario].success++
            } else {
                $this.results[$scenario].fail++
            }
        } catch {
            Write-Host "$scenario request error: $_" -ForegroundColor Red
            $this.results[$scenario].fail++
        }
    }

    [void] GenerateReport() {
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $reportFile = "performance_tests\reports\load_test_results_$timestamp.json"

        # Console report
        Write-Host "`nWebhookShell API Load Test Results" -ForegroundColor Cyan
        Write-Host ("-" * 80)
        Write-Host ("{0,-15} {1,8} {2,8} {3,8} {4,8} {5,8} {6,8} {7,8}" -f "Scenario", "Success", "Fail", "RPS", "Min(ms)", "Max(ms)", "Avg(ms)", "p95(ms)")

        foreach ($scenario in $this.results.Keys) {
            $data = $this.results[$scenario]
            if ($data.latencies.Count -gt 0) {
                $latenciesMs = $data.latencies | ForEach-Object { $_ * 1000 }
                $rps = ($data.success + $data.fail) / $this.durationSeconds
                $minLatency = ($latenciesMs | Measure-Object -Minimum).Minimum
                $maxLatency = ($latenciesMs | Measure-Object -Maximum).Maximum
                $avgLatency = ($latenciesMs | Measure-Object -Average).Average
                $sortedLatencies = $latenciesMs | Sort-Object
                $p95Latency = $sortedLatencies[[math]::Floor($sortedLatencies.Count * 0.95)]

                Write-Host ("{0,-15} {1,8} {2,8} {3,8:F2} {4,8:F2} {5,8:F2} {6,8:F2} {7,8:F2}" -f $scenario, $data.success, $data.fail, $rps, $minLatency, $maxLatency, $avgLatency, $p95Latency)
            } else {
                Write-Host ("{0,-15} {1,8} {2,8} {3,8} {4,8} {5,8} {6,8} {7,8}" -f $scenario, 0, 0, 0, "N/A", "N/A", "N/A", "N/A")
            }
        }

        # JSON report
        $reportData = @{
            test_info = @{
                api_url = $this.apiUrl
                concurrent_users = $this.concurrentUsers
                duration_seconds = $this.durationSeconds
                timestamp = $timestamp
            }
            results = @{}
        }

        foreach ($scenario in $this.results.Keys) {
            $data = $this.results[$scenario]
            $latenciesMs = $data.latencies | ForEach-Object { $_ * 1000 }
            $reportData.results[$scenario] = @{
                success = $data.success
                fail = $data.fail
                latencies_stats = if ($data.latencies.Count -gt 0) {
                    $sortedLatencies = $latenciesMs | Sort-Object
                    @{
                        min_ms = ($latenciesMs | Measure-Object -Minimum).Minimum
                        max_ms = ($latenciesMs | Measure-Object -Maximum).Maximum
                        avg_ms = ($latenciesMs | Measure-Object -Average).Average
                        p95_ms = $sortedLatencies[[math]::Floor($sortedLatencies.Count * 0.95)]
                    }
                } else {
                    @{ min_ms = 0; max_ms = 0; avg_ms = 0; p95_ms = 0 }
                }
            }
        }

        $reportData | ConvertTo-Json -Depth 10 | Out-File $reportFile

        # Markdown summary
        $summaryFile = "performance_tests\reports\summary-windows.md"
        $mdContent = @"
## Performance Test Summary

- Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
- API URL: $($this.apiUrl)
- Concurrent Users: $($this.concurrentUsers)
- Test Duration: $($this.durationSeconds / 60) minutes

### Results Summary

| Scenario | Success | Fail | RPS | Avg (ms) | p95 (ms) |
|----------|---------|------|-----|----------|----------|
"@

        foreach ($scenario in $this.results.Keys) {
            $data = $this.results[$scenario]
            if ($data.latencies.Count -gt 0) {
                $latenciesMs = $data.latencies | ForEach-Object { $_ * 1000 }
                $rps = ($data.success + $data.fail) / $this.durationSeconds
                $avgLatency = ($latenciesMs | Measure-Object -Average).Average
                $p95Latency = ($latenciesMs | Sort-Object)[[math]::Floor($latenciesMs.Count * 0.95)]
                $mdContent += "| $scenario | $($data.success) | $($data.fail) | $($rps.ToString('F2')) | $($avgLatency.ToString('F2')) | $($p95Latency.ToString('F2')) |`n"
            } else {
                $mdContent += "| $scenario | 0 | 0 | 0 | N/A | N/A |`n"
            }
        }

        $mdContent | Out-File $summaryFile
        Write-Host "Load test completed. Results saved to $reportFile" -ForegroundColor Green
    }
}

# Main execution
$tester = [WebhookLoadTester]::new($ApiUrl, $ApiKey, $ConcurrentUsers, $Duration)
$tester.RunTest()