#!/usr/bin/env python3
import argparse
import asyncio
import aiohttp
import json
import time
import statistics
from datetime import datetime
import os
from rich.console import Console
from rich.table import Table
from rich.progress import Progress, TaskID

console = Console()

def main():
    parser = argparse.ArgumentParser(description="WebhookShell API Load Tester")
    parser.add_argument("--api-url", default="http://localhost:80", help="API URL to test")
    parser.add_argument("--api-key", default="24ffc5be-7dd8-479f-898e-27169bf23e7f", help="API key for authentication")
    parser.add_argument("--concurrent-users", type=int, default=50, help="Number of concurrent users")
    parser.add_argument("--duration", type=int, default=5, help="Test duration in minutes")
    
    args = parser.parse_args()
    
    # Ensure reports directory exists
    os.makedirs("reports", exist_ok=True)
    
    tester = WebhookLoadTester(
        api_url=args.api_url,
        api_key=args.api_key,
        concurrent_users=args.concurrent_users,
        duration_minutes=args.duration
    )
    
    asyncio.run(tester.run_test())

class WebhookLoadTester:
    def __init__(self, api_url, api_key, concurrent_users, duration_minutes):
        self.api_url = api_url
        self.api_key = api_key
        self.concurrent_users = concurrent_users
        self.duration_seconds = duration_minutes * 60
        self.results = {
            "GET API": {"success": 0, "fail": 0, "latencies": []},
            "POST API": {"success": 0, "fail": 0, "latencies": []},
            "Background Job": {"success": 0, "fail": 0, "latencies": []}
        }
    
    async def run_test(self):
    console.print(f"[bold green]Starting load test against {self.api_url}/health[/bold green]")
    console.print(f"Concurrent users: {self.concurrent_users}")
    console.print(f"Test duration: {self.duration_seconds // 60} minutes")
    
    # Explicitly disable SSL for HTTP
    async with aiohttp.ClientSession(connector=aiohttp.TCPConnector(ssl=False)) as session:
        with Progress() as progress:
            get_task = progress.add_task("[cyan]GET API Requests...", total=self.duration_seconds)
            post_task = progress.add_task("[magenta]POST API Requests...", total=self.duration_seconds)
            bg_task = progress.add_task("[yellow]Background Job Requests...", total=self.duration_seconds)
            
            # Use /health endpoint if that's the target
            await asyncio.gather(
                self._run_get_scenario(session, progress, get_task, endpoint=f"{self.api_url}/health"),
                self._run_post_scenario(session, progress, post_task, endpoint=f"{self.api_url}/health"),
                self._run_background_job_scenario(session, progress, bg_task, endpoint=f"{self.api_url}/health")
            )
    
    self._generate_report()

    async def _run_get_scenario(self, session, progress, task_id):
        start_time = time.time()
        payload = {
            "script": "health-check-script.ps1",
            "key": self.api_key
        }
        
        # Build query parameters
        query_params = "&".join([f"{k}={v}" for k, v in payload.items()])
        endpoint = f"{self.api_url}/webhook/v1?{query_params}"
        
        while time.time() - start_time < self.duration_seconds:
            # Create a batch of concurrent requests
            tasks = []
            for _ in range(self.concurrent_users // 3):  # Divide users among 3 scenarios
                tasks.append(self._send_get_request(session, endpoint))
            
            # Wait for the batch to complete
            await asyncio.gather(*tasks)
            
            # Update progress
            elapsed = min(time.time() - start_time, self.duration_seconds)
            progress.update(task_id, completed=elapsed)
            
            # Rate limiting: 10 requests per second (per scenario)
            await asyncio.sleep(0.1)

    async def _run_post_scenario(self, session, progress, task_id):
        start_time = time.time()
        payload = {
            "script": "health-check-script.ps1",
            "key": self.api_key
        }
        
        endpoint = f"{self.api_url}/webhook/v1"
        
        while time.time() - start_time < self.duration_seconds:
            # Create a batch of concurrent requests
            tasks = []
            for _ in range(self.concurrent_users // 3):  # Divide users among 3 scenarios
                tasks.append(self._send_post_request(session, endpoint, payload))
            
            # Wait for the batch to complete
            await asyncio.gather(*tasks)
            
            # Update progress
            elapsed = min(time.time() - start_time, self.duration_seconds)
            progress.update(task_id, completed=elapsed)
            
            # Rate limiting: 10 requests per second (per scenario)
            await asyncio.sleep(0.1)

    async def _run_background_job_scenario(self, session, progress, task_id):
        start_time = time.time()
        payload = {
            "script": "health-check-script.ps1",
            "key": self.api_key
        }
        
        endpoint = f"{self.api_url}/jobs/v1/enqueue"
        
        while time.time() - start_time < self.duration_seconds:
            # Create a batch of concurrent requests
            tasks = []
            for _ in range(self.concurrent_users // 3):  # Divide users among 3 scenarios
                tasks.append(self._send_post_request(session, endpoint, payload, "Background Job"))
            
            # Wait for the batch to complete
            await asyncio.gather(*tasks)
            
            # Update progress
            elapsed = min(time.time() - start_time, self.duration_seconds)
            progress.update(task_id, completed=elapsed)
            
            # Rate limiting: 15 requests per second (per scenario)
            await asyncio.sleep(0.067)  # ~15 rps

    async def _send_get_request(self, session, url):
        start_time = time.time()
        try:
            async with session.get(url, headers={"Accept": "application/json"}) as response:
                await response.read()  # Read the full response
                latency = time.time() - start_time
                self.results["GET API"]["latencies"].append(latency)
                
                if response.status < 400:
                    self.results["GET API"]["success"] += 1
                else:
                    self.results["GET API"]["fail"] += 1
        except Exception as e:
            console.print(f"[red]GET request error: {e}[/red]")
            self.results["GET API"]["fail"] += 1

    async def _send_post_request(self, session, url, payload, scenario="POST API"):
        start_time = time.time()
        try:
            async with session.post(
                url, 
                json=payload,
                headers={
                    "Content-Type": "application/json",
                    "Accept": "application/json"
                }
            ) as response:
                await response.read()  # Read the full response
                latency = time.time() - start_time
                self.results[scenario]["latencies"].append(latency)
                
                if response.status < 400:
                    self.results[scenario]["success"] += 1
                else:
                    self.results[scenario]["fail"] += 1
        except Exception as e:
            console.print(f"[red]{scenario} request error: {e}[/red]")
            self.results[scenario]["fail"] += 1

    def _generate_report(self):
        # Create reports directory
        os.makedirs("reports", exist_ok=True)
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        
        # Create console report
        table = Table(title="WebhookShell API Load Test Results")
        table.add_column("Scenario", style="cyan")
        table.add_column("Success", style="green")
        table.add_column("Fail", style="red")
        table.add_column("RPS", style="yellow")
        table.add_column("Min (ms)", style="blue")
        table.add_column("Max (ms)", style="blue")
        table.add_column("Avg (ms)", style="blue")
        table.add_column("p95 (ms)", style="magenta")
        
        for scenario, data in self.results.items():
            if data["latencies"]:
                latencies_ms = [l * 1000 for l in data["latencies"]]
                rps = (data["success"] + data["fail"]) / self.duration_seconds
                
                min_latency = min(latencies_ms)
                max_latency = max(latencies_ms)
                avg_latency = statistics.mean(latencies_ms)
                p95_latency = sorted(latencies_ms)[int(len(latencies_ms) * 0.95)]
                
                table.add_row(
                    scenario,
                    str(data["success"]),
                    str(data["fail"]),
                    f"{rps:.2f}",
                    f"{min_latency:.2f}",
                    f"{max_latency:.2f}",
                    f"{avg_latency:.2f}",
                    f"{p95_latency:.2f}"
                )
            else:
                table.add_row(scenario, "0", "0", "0", "N/A", "N/A", "N/A", "N/A")
        
        console.print(table)
        
        # Save results to file
        report_file = f"reports/load_test_results_{timestamp}.json"
        with open(report_file, "w") as f:
            json.dump({
                "test_info": {
                    "api_url": self.api_url,
                    "concurrent_users": self.concurrent_users,
                    "duration_seconds": self.duration_seconds,
                    "timestamp": timestamp
                },
                "results": {
                    scenario: {
                        "success": data["success"],
                        "fail": data["fail"],
                        "latencies_stats": {
                            "min_ms": min(data["latencies"]) * 1000 if data["latencies"] else 0,
                            "max_ms": max(data["latencies"]) * 1000 if data["latencies"] else 0,
                            "avg_ms": statistics.mean(data["latencies"]) * 1000 if data["latencies"] else 0,
                            "p95_ms": sorted([l * 1000 for l in data["latencies"]])[int(len(data["latencies"]) * 0.95)] if data["latencies"] else 0
                        }
                    } for scenario, data in self.results.items()
                }
            }, f, indent=2)
        
        # Generate Markdown report summary for GitHub
        summary_file = "reports/summary.md"
        with open(summary_file, "w") as f:
            f.write("## Performance Test Summary\n\n")
            f.write(f"- Date: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n")
            f.write(f"- API URL: {self.api_url}\n")
            f.write(f"- Concurrent Users: {self.concurrent_users}\n")
            f.write(f"- Test Duration: {self.duration_seconds // 60} minutes\n\n")
            
            f.write("### Results Summary\n\n")
            f.write("| Scenario | Success | Fail | RPS | Avg (ms) | p95 (ms) |\n")
            f.write("|----------|---------|------|-----|----------|----------|\n")
            
            for scenario, data in self.results.items():
                if data["latencies"]:
                    latencies_ms = [l * 1000 for l in data["latencies"]]
                    rps = (data["success"] + data["fail"]) / self.duration_seconds
                    avg_latency = statistics.mean(latencies_ms)
                    p95_latency = sorted(latencies_ms)[int(len(latencies_ms) * 0.95)]
                    
                    f.write(f"| {scenario}  |     {data['success']}    |     {data['fail']}     |     {rps:.2f}     |     {avg_latency:.2f}     |     {p95_latency:.2f}     |\n")
                else:
                    f.write(f"| {scenario}  |     0     |     0     |     0     |     N/A     |     N/A    |\n")
        
        console.print(f"[bold green]Load test completed. Results saved to {report_file}[/bold green]")

if __name__ == "__main__":
    main()