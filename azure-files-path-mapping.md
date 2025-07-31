# Azure Files Path Mapping Confusion Explained

## The Problem

The worker and agent containers mount the SAME Azure Files share at DIFFERENT paths:

```
Azure Files Storage Account: voicecodeworkspaces
└── File Share: workspaces
    └── (root of share)
        └── 1/  (worker 1's directory)
            └── (files should go here)
```

## Current Mount Configuration

### Worker Container
- Mount point: `/workspaces`
- Sees: `/workspaces/1/` as worker 1's directory
- Expects files at: `/workspaces/1/options_greeks.py`

### Agent Container
- Mount point: `/project`
- Writes files to: `/project/options_greeks.py`
- Does NOT automatically create or use the `1/` subdirectory

## The Mismatch

When claude-agent writes files:
1. Worker tells agent to use workspace "1"
2. Agent writes to `/project/options_greeks.py`
3. This creates files at the ROOT of the Azure Files share
4. Worker looks in `/workspaces/1/` (which maps to `<share>/1/`)
5. Files aren't there because they're at `<share>/options_greeks.py`

## Visual Representation

```
What's Happening:
================
Azure Files Share: "workspaces"
├── 1/                    (empty - created by worker)
├── options_greeks.py     (written by agent at root)
├── requirements.txt      (written by agent at root)
└── README.md            (written by agent at root)

Worker sees:              Agent sees:
/workspaces/             /project/
├── 1/                   ├── options_greeks.py
├── options_greeks.py    ├── requirements.txt
├── requirements.txt     └── README.md
└── README.md

What Should Happen:
==================
Azure Files Share: "workspaces"
└── 1/                    
    ├── options_greeks.py
    ├── requirements.txt
    └── README.md

Worker sees:              Agent sees:
/workspaces/             /project/
└── 1/                   └── 1/
    ├── options_greeks.py     ├── options_greeks.py
    ├── requirements.txt      ├── requirements.txt
    └── README.md            └── README.md
```

## The Solution

There are several ways to fix this:

### Option 1: Change Agent's Mount Point
Mount the agent at `/project` but tell it to write to subdirectory:
- Agent mount: `/project` → Azure Files root
- Agent writes to: `/project/1/options_greeks.py`

### Option 2: Change Agent's Mount to Match Worker's Structure
- Agent mount: `/workspaces` (same as worker)
- Both see the same paths
- Agent writes to: `/workspaces/1/options_greeks.py`

### Option 3: Mount Agent Directly to Worker's Subdirectory
- Create separate mount for agent pointing to `/1` in the share
- Agent mount: `/project` → Azure Files `/1/`
- Agent writes to: `/project/options_greeks.py` → Goes to `/1/options_greeks.py`

### Option 4: Modify Worker Service to Pass Correct Path
- Worker tells agent the correct subdirectory path
- In BuildSdkPrompt, change working directory to include workspace ID
- Pass `workingDirectory: "/project/1"` to agent

## Current Workaround Attempt

The worker service sets:
```csharp
var workspaceRoot = Path.Combine(_options.WorkspaceBasePath, task.WorkspaceId);
// workspaceRoot = "/workspaces/1"
```

But the agent is configured with:
```javascript
const PROJECT_ROOT = process.env.WORKSPACE_ROOT || '/project';
```

The agent's `WORKSPACE_ROOT` environment variable is `/project`, which doesn't include the workspace ID subdirectory.