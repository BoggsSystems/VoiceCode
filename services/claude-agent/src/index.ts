import express from 'express';
import dotenv from 'dotenv';
import { ClaudeAgent } from './services/ClaudeAgent.js';
import { ServiceBusPublisher } from './services/ServiceBusPublisher.js';
import { logger } from './utils/logger.js';

// Load environment variables
dotenv.config();

const app = express();
const port = process.env.PORT || 3000;

// Middleware
app.use(express.json({ limit: '10mb' }));

// Request logging
app.use((req, _, next) => {
  logger.info({ 
    method: req.method, 
    url: req.url,
    headers: req.headers
  }, 'Incoming request');
  next();
});

// Initialize services
const agent = new ClaudeAgent({
  apiKey: process.env.ANTHROPIC_API_KEY || '',
  model: process.env.CLAUDE_MODEL,
  maxTokens: parseInt(process.env.CLAUDE_MAX_TOKENS || '4096'),
  temperature: parseFloat(process.env.CLAUDE_TEMPERATURE || '0')
});

const serviceBusPublisher = new ServiceBusPublisher();

// Set up event handlers for real-time updates
agent.on('tool:executed', async (event) => {
  logger.debug({ event }, 'Tool executed');
  
  // Publish to Service Bus for Observer Service
  await serviceBusPublisher.publishEvent({
    id: `tool-${Date.now()}`,
    sessionId: '', // Will be set from request context
    workerId: process.env.WORKER_ID || 'claude-agent',
    eventType: 'ToolExecution',
    operation: event.tool,
    details: JSON.stringify(event.input),
    metadata: event.output,
    timestamp: new Date()
  });
});

// Health check
app.get('/health', (_, res) => {
  res.json({ 
    status: 'healthy',
    service: 'claude-agent',
    version: '1.0.0',
    timestamp: new Date().toISOString()
  });
});

// SDK availability check (expected by worker)
app.get('/api/sdk/available', (_, res) => {
  res.json({ 
    available: true,
    version: '1.0.0'
  });
});

// Session management endpoints (expected by worker)
app.post('/api/sdk/sessions', (req, res) => {
  const { WorkspaceId } = req.body;
  const sessionId = `session-${WorkspaceId}-${Date.now()}`;
  
  logger.info({ WorkspaceId, sessionId }, 'Creating session (stub)');
  
  res.json({
    sessionId: sessionId
  });
});

app.delete('/api/sdk/sessions/:sessionId', (req, res) => {
  const { sessionId } = req.params;
  
  logger.info({ sessionId }, 'Closing session (stub)');
  
  res.status(204).send();
});

// Main task endpoint
app.post('/task', async (req, res) => {
  try {
    const { message, sessionId, workingDirectory } = req.body;
    
    if (!message) {
      return res.status(400).json({ 
        error: 'Message is required' 
      });
    }

    logger.info({ message, sessionId, workingDirectory }, 'Processing task');
    
    // Execute the task
    const result = await agent.executeTask(message);
    
    // Publish completion event
    await serviceBusPublisher.publishEvent({
      id: `task-complete-${Date.now()}`,
      sessionId: sessionId || '',
      workerId: process.env.WORKER_ID || 'claude-agent',
      eventType: 'TaskComplete',
      operation: 'task',
      details: message,
      metadata: {
        success: result.success,
        toolCallCount: result.toolCalls?.length || 0,
        usage: result.usage
      },
      timestamp: new Date()
    });
    
    return res.json({
      success: result.success,
      response: result.response,
      error: result.error,
      toolCalls: result.toolCalls,
      usage: result.usage
    });
  } catch (error: unknown) {
    logger.error({ error }, 'Task processing failed');
    return res.status(500).json({
      success: false,
      error: (error as any) instanceof Error ? (error as Error).message : 'Internal server error'
    });
  }
});

// Tool execution endpoint (for testing individual tools)
app.post('/tool/:toolName', async (req, res) => {
  try {
    const { toolName } = req.params;
    const input = req.body;
    
    logger.info({ toolName, input }, 'Executing tool directly');
    
    // Import fileTools dynamically
    const fileTools = await import('./tools/fileTools.js');
    
    let result;
    switch (toolName) {
      case 'list_dir':
        result = await fileTools.listDir(input.path);
        break;
      case 'read_file':
        result = await fileTools.readFile(input.path);
        break;
      case 'write_file':
        result = await fileTools.writeFile(input.path, input.contents);
        break;
      case 'search_codebase':
        result = await fileTools.searchCodebase(input.query, input.filePattern);
        break;
      case 'get_project_summary':
        result = await fileTools.getProjectSummary();
        break;
      default:
        return res.status(404).json({ error: 'Tool not found' });
    }
    
    return res.json(result);
  } catch (error: unknown) {
    logger.error({ error }, 'Tool execution failed');
    return res.status(500).json({
      success: false,
      error: (error as any) instanceof Error ? (error as Error).message : 'Internal server error'
    });
  }
});

// Root endpoint
app.get('/', (_, res) => {
  res.json({
    service: 'Claude Agent',
    version: '1.0.0',
    endpoints: {
      health: '/health',
      task: '/task',
      tool: '/tool/:toolName'
    },
    tools: [
      'list_dir',
      'read_file',
      'write_file',
      'append_file',
      'delete_file',
      'move_file',
      'create_directory',
      'search_codebase',
      'get_project_summary',
      'get_file_tree',
      'run_shell'
    ]
  });
});

// Error handling
app.use((err: Error, _req: express.Request, res: express.Response, _next: express.NextFunction) => {
  logger.error({ error: err }, 'Unhandled error');
  res.status(500).json({
    error: 'Internal server error',
    message: process.env.NODE_ENV === 'development' ? err.message : undefined
  });
});

// Start server
const server = app.listen(port, () => {
  logger.info({ port }, 'Claude Agent started');
  logger.info({ 
    anthropicKey: !!process.env.ANTHROPIC_API_KEY,
    projectRoot: process.env.WORKSPACE_ROOT || '/project',
    serviceBus: !!process.env.AZURE_SERVICE_BUS_CONNECTION_STRING
  }, 'Service configuration');
});

// Graceful shutdown
process.on('SIGTERM', async () => {
  logger.info('SIGTERM received, shutting down gracefully');
  
  server.close(() => {
    logger.info('HTTP server closed');
  });
  
  await serviceBusPublisher.close();
  process.exit(0);
});

export { app };