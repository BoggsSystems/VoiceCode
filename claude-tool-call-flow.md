# Claude Tool Call Translation Flow

## How Claude's Tool Calls Become Local File Operations

### 1. Claude Receives the Request

When the agent calls the Anthropic API with:
```javascript
await this.anthropic.messages.create({
  model: 'claude-3-5-sonnet-20241022',
  messages: [{
    role: 'user',
    content: 'Generate a Python application that calculates Greeks for options'
  }],
  tools: toolDefinitions,
  system: 'You are Claude, an AI assistant with access to a codebase mounted at /project...'
});
```

### 2. Claude's Response Format

Claude responds with a structured message containing tool use blocks:

```json
{
  "content": [
    {
      "type": "text",
      "text": "I'll create a Python application for calculating options Greeks..."
    },
    {
      "type": "tool_use",
      "id": "toolu_01ABC123...",
      "name": "write_file",
      "input": {
        "path": "options_greeks.py",
        "contents": "import numpy as np\nfrom scipy.stats import norm\n\nclass OptionsGreeksCalculator:\n    def __init__(self, S, K, T, r, sigma):\n        \"\"\"\n        S: Current stock price\n        K: Strike price\n        T: Time to maturity (years)\n        r: Risk-free rate\n        sigma: Volatility\n        \"\"\"\n        self.S = S\n        self.K = K\n        self.T = T\n        self.r = r\n        self.sigma = sigma\n    ..."
      }
    },
    {
      "type": "tool_use",
      "id": "toolu_01ABC124...",
      "name": "write_file",
      "input": {
        "path": "requirements.txt",
        "contents": "numpy>=1.21.0\nscipy>=1.7.0"
      }
    }
  ],
  "usage": {
    "input_tokens": 245,
    "output_tokens": 892
  }
}
```

### 3. Tool Call Processing in ClaudeAgent

The `processResponse` method in ClaudeAgent.ts:

```typescript
// Extract tool use blocks from Claude's response
const toolUses = response.content.filter(
  (content): content is any => content.type === 'tool_use'
);

// Execute each tool call
for (const toolUse of toolUses) {
  // toolUse = {
  //   type: 'tool_use',
  //   id: 'toolu_01ABC123...',
  //   name: 'write_file',
  //   input: { path: 'options_greeks.py', contents: '...' }
  // }
  
  const result = await this.executeTool(toolUse.name, toolUse.input);
}
```

### 4. Tool Execution Dispatch

The `executeTool` method maps tool names to actual functions:

```typescript
private async executeTool(toolName: string, input: any): Promise<any> {
  switch (toolName) {
    case 'write_file':
      return await fileTools.writeFile(input.path, input.contents);
    case 'read_file':
      return await fileTools.readFile(input.path);
    // ... other tools
  }
}
```

### 5. File System Operations

In fileTools.ts, the actual file operation happens:

```typescript
export async function writeFile(filePath: string, contents: string): Promise<ToolResult> {
  // Convert relative path to absolute path
  // filePath = 'options_greeks.py'
  const fullPath = safePath(filePath);
  // fullPath = '/project/options_greeks.py'
  
  // Ensure directory exists
  const dir = path.dirname(fullPath);
  await fs.mkdir(dir, { recursive: true });
  
  // Write the file
  await fs.writeFile(fullPath, contents, 'utf-8');
  
  return { 
    success: true, 
    data: `File written: ${filePath}` 
  };
}
```

### 6. Tool Results Feed Back to Claude

After executing tools, results are sent back to Claude:

```typescript
// Tool execution result
const result = {
  success: true,
  data: 'File written: options_greeks.py'
};

// Add to conversation for Claude's next response
toolResults.push({
  role: 'user',
  content: [{
    type: 'tool_result',
    tool_use_id: toolUse.id,
    content: JSON.stringify(result)
  }]
});

// Get Claude's next response with tool results
const nextResponse = await this.anthropic.messages.create({
  messages: [
    ...previousMessages,
    { role: 'assistant', content: response.content },
    ...toolResults
  ],
  tools: toolDefinitions
});
```

### 7. Complete Example Flow

For "Generate a Python application that calculates Greeks for options":

1. **Claude's Initial Response**:
   - Text: "I'll create a Python application..."
   - Tool call 1: write_file('options_greeks.py', full_code)
   - Tool call 2: write_file('requirements.txt', dependencies)
   - Tool call 3: write_file('README.md', documentation)
   - Tool call 4: write_file('example.py', usage_example)

2. **Local Execution**:
   - `/project/options_greeks.py` → Created with Black-Scholes implementation
   - `/project/requirements.txt` → Created with numpy, scipy
   - `/project/README.md` → Created with usage instructions
   - `/project/example.py` → Created with example code

3. **Tool Results Sent Back**:
   ```json
   [
     { "success": true, "data": "File written: options_greeks.py" },
     { "success": true, "data": "File written: requirements.txt" },
     { "success": true, "data": "File written: README.md" },
     { "success": true, "data": "File written: example.py" }
   ]
   ```

4. **Claude's Final Response**:
   "I've created a complete Python application for calculating options Greeks. The application includes:
   - `options_greeks.py`: Main calculator class with methods for Delta, Gamma, Theta, Vega, and Rho
   - `requirements.txt`: Dependencies (numpy and scipy)
   - `README.md`: Documentation and usage instructions
   - `example.py`: Example usage of the calculator"

### Key Points:

1. **Claude Never Executes Code**: Claude only responds with structured tool calls
2. **Local Agent Executes**: The claude-agent service executes the actual file operations
3. **Path Translation**: Relative paths from Claude become absolute paths on the file system
4. **Feedback Loop**: Tool results are fed back to Claude for context
5. **Multiple Rounds**: Claude can request more tools based on previous results
6. **Safety**: The `safePath` function prevents directory traversal attacks