import Anthropic from '@anthropic-ai/sdk';
import { toolDefinitions } from '../tools/toolDefinitions.js';
import * as fileTools from '../tools/fileTools.js';
import { logger } from '../utils/logger.js';
import { EventEmitter } from 'events';

export interface AgentOptions {
  apiKey: string;
  model?: string;
  maxTokens?: number;
  temperature?: number;
}

export interface TaskResult {
  success: boolean;
  response?: string;
  toolCalls?: Array<{
    tool: string;
    input: any;
    output: any;
  }>;
  error?: string;
  usage?: {
    inputTokens: number;
    outputTokens: number;
  };
}

export class ClaudeAgent extends EventEmitter {
  private anthropic: Anthropic;
  private model: string;
  private maxTokens: number;
  private temperature: number;

  constructor(options: AgentOptions) {
    super();
    
    this.anthropic = new Anthropic({
      apiKey: options.apiKey,
    });
    
    this.model = options.model || 'claude-3-opus-20240229';
    this.maxTokens = options.maxTokens || 4096;
    this.temperature = options.temperature || 0;
  }

  async executeTask(message: string): Promise<TaskResult> {
    const toolCalls: TaskResult['toolCalls'] = [];
    
    try {
      logger.info({ message }, 'Executing task');
      
      // Build system prompt
      const systemPrompt = this.buildSystemPrompt();
      
      // Initial message to Claude with tools
      const response = await this.anthropic.messages.create({
        model: this.model,
        max_tokens: this.maxTokens,
        temperature: this.temperature,
        system: systemPrompt,
        messages: [
          {
            role: 'user',
            content: message
          }
        ],
        tools: toolDefinitions as any
      });

      // Process the response and handle tool calls
      let finalResponse = await this.processResponse(response, toolCalls);
      
      return {
        success: true,
        response: this.extractTextContent(finalResponse),
        toolCalls,
        usage: {
          inputTokens: finalResponse.usage.input_tokens,
          outputTokens: finalResponse.usage.output_tokens
        }
      };
    } catch (error) {
      logger.error({ error }, 'Task execution failed');
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error',
        toolCalls
      };
    }
  }

  private async processResponse(
    response: Anthropic.Message,
    toolCalls: TaskResult['toolCalls'] = []
  ): Promise<Anthropic.Message> {
    // Check if Claude wants to use tools
    const toolUses = response.content.filter(
      (content): content is any => content.type === 'tool_use'
    );

    if (toolUses.length === 0) {
      // No tools to call, return the response
      return response;
    }

    // Execute each tool call
    const toolResults: any[] = [];
    
    for (const toolUse of toolUses) {
      logger.debug({ tool: toolUse.name, input: toolUse.input }, 'Executing tool');
      
      const result = await this.executeTool(toolUse.name, toolUse.input);
      
      // Record the tool call
      toolCalls?.push({
        tool: toolUse.name,
        input: toolUse.input,
        output: result
      });

      // Emit event for streaming
      this.emit('tool:executed', {
        tool: toolUse.name,
        input: toolUse.input,
        output: result
      });

      // Add tool result to the conversation
      toolResults.push({
        role: 'user' as const,
        content: [
          {
            type: 'tool_result' as const,
            tool_use_id: toolUse.id,
            content: JSON.stringify(result)
          }
        ]
      });
    }

    // Continue the conversation with tool results
    const messages: any[] = [
      {
        role: 'user',
        content: response.content.filter(c => c.type === 'text').map(c => ({
          type: 'text' as const,
          text: (c as any).text
        }))
      },
      {
        role: 'assistant',
        content: response.content
      },
      ...toolResults
    ];

    // Get Claude's next response
    const nextResponse = await this.anthropic.messages.create({
      model: this.model,
      max_tokens: this.maxTokens,
      temperature: this.temperature,
      system: this.buildSystemPrompt(),
      messages,
      tools: toolDefinitions as any
    });

    // Recursively process if more tools are needed
    return this.processResponse(nextResponse, toolCalls);
  }

  private async executeTool(toolName: string, input: any): Promise<any> {
    try {
      switch (toolName) {
        case 'list_dir':
          return await fileTools.listDir(input.path);
        
        case 'read_file':
          return await fileTools.readFile(input.path);
        
        case 'write_file':
          return await fileTools.writeFile(input.path, input.contents);
        
        case 'append_file':
          return await fileTools.appendFile(input.path, input.contents);
        
        case 'delete_file':
          return await fileTools.deleteFile(input.path);
        
        case 'move_file':
          return await fileTools.moveFile(input.from, input.to);
        
        case 'create_directory':
          return await fileTools.createDirectory(input.path);
        
        case 'search_codebase':
          return await fileTools.searchCodebase(input.query, input.filePattern);
        
        case 'get_project_summary':
          return await fileTools.getProjectSummary();
        
        case 'get_file_tree':
          return await fileTools.getFileTree(input.path, input.maxDepth);
        
        case 'run_shell':
          return await fileTools.runShell(input.command, input.cwd);
        
        default:
          return { success: false, error: `Unknown tool: ${toolName}` };
      }
    } catch (error) {
      logger.error({ error, tool: toolName }, 'Tool execution failed');
      return { 
        success: false, 
        error: error instanceof Error ? error.message : 'Unknown error' 
      };
    }
  }

  private buildSystemPrompt(): string {
    return `You are Claude, an AI assistant with access to a codebase mounted at /project.
You can explore, analyze, and modify files in this codebase using the provided tools.

Important guidelines:
1. Always start by understanding the project structure using get_project_summary or list_dir
2. Read files to understand the codebase before making changes
3. When modifying files, preserve existing formatting and style
4. Explain your reasoning and findings clearly
5. Be thorough in your analysis

The user cannot see which tools you're calling - focus on providing clear explanations of what you discover and what changes you make.`;
  }

  private extractTextContent(response: Anthropic.Message): string {
    return response.content
      .filter((content): content is Anthropic.TextBlock => content.type === 'text')
      .map(content => content.text)
      .join('\n');
  }
}