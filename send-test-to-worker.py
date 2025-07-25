#!/usr/bin/env python3
import json
import requests
import time

# Send a test message to worker-1 using the correct queue name
orchestrator_url = "https://voicecode-orchestrator.orangewater-a2f689a8.eastus.azurecontainerapps.io"

# Test command for worker 1
command = {
    "voiceCommand": "In worker 1, list all the files in the root directory and tell me what this project is about"
}

print("🚀 Sending test command to worker-1...")
response = requests.post(
    f"{orchestrator_url}/api/orchestrator/submit",
    json=command,
    headers={"Content-Type": "application/json"}
)

print(f"Response: {response.status_code}")
print(json.dumps(response.json(), indent=2))

if response.status_code == 200:
    task_id = response.json().get("taskId")
    print(f"\n✅ Task submitted: {task_id}")
    print("⏳ The worker should process this and attempt to use Claude Code...")
    print("📋 Check worker logs to see the result")
else:
    print("\n❌ Failed to submit task")