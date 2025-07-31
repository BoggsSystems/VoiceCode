// Tool definitions for Claude agent

export const toolDefinitions: any[] = [
  {
    name: 'list_dir',
    description: 'List files and directories in a given path',
    input_schema: {
      type: 'object',
      properties: {
        path: {
          type: 'string',
          description: 'Directory path relative to /project root (use "." for root)'
        }
      },
      required: ['path']
    }
  },
  {
    name: 'read_file',
    description: 'Read the contents of a file',
    input_schema: {
      type: 'object',
      properties: {
        path: {
          type: 'string',
          description: 'File path relative to /project root'
        }
      },
      required: ['path']
    }
  },
  {
    name: 'write_file',
    description: 'Write or overwrite a file with new contents',
    input_schema: {
      type: 'object',
      properties: {
        path: {
          type: 'string',
          description: 'File path relative to /project root'
        },
        contents: {
          type: 'string',
          description: 'Contents to write to the file'
        }
      },
      required: ['path', 'contents']
    }
  },
  {
    name: 'append_file',
    description: 'Append contents to an existing file',
    input_schema: {
      type: 'object',
      properties: {
        path: {
          type: 'string',
          description: 'File path relative to /project root'
        },
        contents: {
          type: 'string',
          description: 'Contents to append to the file'
        }
      },
      required: ['path', 'contents']
    }
  },
  {
    name: 'delete_file',
    description: 'Delete a file',
    input_schema: {
      type: 'object',
      properties: {
        path: {
          type: 'string',
          description: 'File path relative to /project root'
        }
      },
      required: ['path']
    }
  },
  {
    name: 'move_file',
    description: 'Move or rename a file',
    input_schema: {
      type: 'object',
      properties: {
        from: {
          type: 'string',
          description: 'Source file path relative to /project root'
        },
        to: {
          type: 'string',
          description: 'Destination file path relative to /project root'
        }
      },
      required: ['from', 'to']
    }
  },
  {
    name: 'create_directory',
    description: 'Create a new directory',
    input_schema: {
      type: 'object',
      properties: {
        path: {
          type: 'string',
          description: 'Directory path relative to /project root'
        }
      },
      required: ['path']
    }
  },
  {
    name: 'search_codebase',
    description: 'Search for text across all files in the codebase',
    input_schema: {
      type: 'object',
      properties: {
        query: {
          type: 'string',
          description: 'Text to search for'
        },
        filePattern: {
          type: 'string',
          description: 'Optional file pattern to search in (e.g., "*.ts" for TypeScript files)'
        }
      },
      required: ['query']
    }
  },
  {
    name: 'get_project_summary',
    description: 'Get a high-level summary of the project structure, dependencies, and main files',
    input_schema: {
      type: 'object',
      properties: {}
    }
  },
  {
    name: 'get_file_tree',
    description: 'Get a tree view of the project structure',
    input_schema: {
      type: 'object',
      properties: {
        path: {
          type: 'string',
          description: 'Starting directory path (default: ".")',
          default: '.'
        },
        maxDepth: {
          type: 'number',
          description: 'Maximum depth to traverse (default: 3)',
          default: 3
        }
      }
    }
  },
  {
    name: 'run_shell',
    description: 'Run a safe shell command (limited to read-only commands)',
    input_schema: {
      type: 'object',
      properties: {
        command: {
          type: 'string',
          description: 'Shell command to run (only safe read commands allowed)'
        },
        cwd: {
          type: 'string',
          description: 'Working directory relative to /project (optional)'
        }
      },
      required: ['command']
    }
  }
];