import fs from 'fs/promises';
import path from 'path';
import { glob } from 'glob';
import { exec } from 'child_process';
import { promisify } from 'util';
import {
  FileInfo,
  SearchResult,
  ProjectSummary,
  ToolResult
} from '../types/tools.js';

const execAsync = promisify(exec);
const PROJECT_ROOT = '/project';

// Ensure path is within project bounds
function safePath(userPath: string): string {
  const resolved = path.resolve(PROJECT_ROOT, userPath);
  if (!resolved.startsWith(PROJECT_ROOT)) {
    throw new Error('Path traversal attempt detected');
  }
  return resolved;
}

export async function listDir(dirPath: string): Promise<ToolResult> {
  try {
    const fullPath = safePath(dirPath);
    const entries = await fs.readdir(fullPath, { withFileTypes: true });
    
    const files: FileInfo[] = await Promise.all(
      entries.map(async (entry) => {
        const entryPath = path.join(fullPath, entry.name);
        const relativePath = path.relative(PROJECT_ROOT, entryPath);
        
        if (entry.isDirectory()) {
          return {
            name: entry.name,
            path: relativePath,
            type: 'directory' as const
          };
        } else {
          const stats = await fs.stat(entryPath);
          return {
            name: entry.name,
            path: relativePath,
            type: 'file' as const,
            size: stats.size,
            modified: stats.mtime
          };
        }
      })
    );
    
    return { success: true, data: files };
  } catch (error) {
    return { 
      success: false, 
      error: error instanceof Error ? error.message : 'Unknown error' 
    };
  }
}

export async function readFile(filePath: string): Promise<ToolResult> {
  try {
    const fullPath = safePath(filePath);
    const content = await fs.readFile(fullPath, 'utf-8');
    return { success: true, data: content };
  } catch (error) {
    return { 
      success: false, 
      error: error instanceof Error ? error.message : 'Unknown error' 
    };
  }
}

export async function writeFile(filePath: string, contents: string): Promise<ToolResult> {
  try {
    const fullPath = safePath(filePath);
    
    // Ensure directory exists
    const dir = path.dirname(fullPath);
    await fs.mkdir(dir, { recursive: true });
    
    await fs.writeFile(fullPath, contents, 'utf-8');
    return { success: true, data: `File written: ${filePath}` };
  } catch (error) {
    return { 
      success: false, 
      error: error instanceof Error ? error.message : 'Unknown error' 
    };
  }
}

export async function appendFile(filePath: string, contents: string): Promise<ToolResult> {
  try {
    const fullPath = safePath(filePath);
    await fs.appendFile(fullPath, contents, 'utf-8');
    return { success: true, data: `Content appended to: ${filePath}` };
  } catch (error) {
    return { 
      success: false, 
      error: error instanceof Error ? error.message : 'Unknown error' 
    };
  }
}

export async function deleteFile(filePath: string): Promise<ToolResult> {
  try {
    const fullPath = safePath(filePath);
    await fs.unlink(fullPath);
    return { success: true, data: `File deleted: ${filePath}` };
  } catch (error) {
    return { 
      success: false, 
      error: error instanceof Error ? error.message : 'Unknown error' 
    };
  }
}

export async function moveFile(from: string, to: string): Promise<ToolResult> {
  try {
    const fromPath = safePath(from);
    const toPath = safePath(to);
    
    // Ensure destination directory exists
    const toDir = path.dirname(toPath);
    await fs.mkdir(toDir, { recursive: true });
    
    await fs.rename(fromPath, toPath);
    return { success: true, data: `Moved ${from} to ${to}` };
  } catch (error) {
    return { 
      success: false, 
      error: error instanceof Error ? error.message : 'Unknown error' 
    };
  }
}

export async function createDirectory(dirPath: string): Promise<ToolResult> {
  try {
    const fullPath = safePath(dirPath);
    await fs.mkdir(fullPath, { recursive: true });
    return { success: true, data: `Directory created: ${dirPath}` };
  } catch (error) {
    return { 
      success: false, 
      error: error instanceof Error ? error.message : 'Unknown error' 
    };
  }
}

export async function searchCodebase(query: string, filePattern?: string): Promise<ToolResult> {
  try {
    const pattern = filePattern || '**/*';
    const files = await glob(pattern, { 
      cwd: PROJECT_ROOT,
      ignore: ['**/node_modules/**', '**/.git/**', '**/dist/**', '**/build/**'],
      nodir: true
    });
    
    const results: SearchResult[] = [];
    
    for (const file of files) {
      const fullPath = path.join(PROJECT_ROOT, file);
      try {
        const content = await fs.readFile(fullPath, 'utf-8');
        const lines = content.split('\n');
        
        lines.forEach((line, index) => {
          if (line.toLowerCase().includes(query.toLowerCase())) {
            results.push({
              file,
              line: index + 1,
              content: line.trim(),
              match: query
            });
          }
        });
      } catch {
        // Skip files that can't be read (binary files, etc.)
      }
    }
    
    return { success: true, data: results };
  } catch (error) {
    return { 
      success: false, 
      error: error instanceof Error ? error.message : 'Unknown error' 
    };
  }
}

export async function getProjectSummary(): Promise<ToolResult> {
  try {
    // Check for package.json
    let packageJson: any = {};
    try {
      const pkgContent = await fs.readFile(path.join(PROJECT_ROOT, 'package.json'), 'utf-8');
      packageJson = JSON.parse(pkgContent);
    } catch {
      // No package.json
    }
    
    // Get all files
    const files = await glob('**/*', { 
      cwd: PROJECT_ROOT,
      ignore: ['**/node_modules/**', '**/.git/**'],
      nodir: true
    });
    
    // Count file types
    const fileTypes: Record<string, number> = {};
    files.forEach(file => {
      const ext = path.extname(file).toLowerCase() || 'no-extension';
      fileTypes[ext] = (fileTypes[ext] || 0) + 1;
    });
    
    // Detect main languages
    const languageExtensions: Record<string, string> = {
      '.ts': 'TypeScript',
      '.tsx': 'TypeScript React',
      '.js': 'JavaScript',
      '.jsx': 'JavaScript React',
      '.py': 'Python',
      '.java': 'Java',
      '.cs': 'C#',
      '.go': 'Go',
      '.rs': 'Rust',
      '.cpp': 'C++',
      '.c': 'C',
      '.rb': 'Ruby',
      '.php': 'PHP'
    };
    
    const mainLanguages = Object.entries(fileTypes)
      .filter(([ext]) => languageExtensions[ext])
      .sort(([, a], [, b]) => b - a)
      .slice(0, 3)
      .map(([ext]) => languageExtensions[ext]);
    
    // Find entry points
    const entryPoints: string[] = [];
    const commonEntryFiles = ['index.js', 'index.ts', 'main.js', 'main.ts', 'app.js', 'app.ts', 'server.js', 'server.ts'];
    
    for (const entry of commonEntryFiles) {
      if (files.includes(entry) || files.includes(`src/${entry}`)) {
        entryPoints.push(entry);
      }
    }
    
    // Get unique directories
    const directories = [...new Set(files.map(f => path.dirname(f)))].filter(d => d !== '.');
    
    const summary: ProjectSummary = {
      type: packageJson.name ? 'Node.js/npm project' : 'Generic project',
      mainLanguages,
      entryPoints,
      dependencies: packageJson.dependencies || {},
      structure: {
        directories,
        totalFiles: files.length,
        fileTypes
      }
    };
    
    return { success: true, data: summary };
  } catch (error) {
    return { 
      success: false, 
      error: error instanceof Error ? error.message : 'Unknown error' 
    };
  }
}

export async function runShell(command: string, cwd?: string): Promise<ToolResult> {
  try {
    // Sanitize command - only allow certain safe commands
    const allowedCommands = ['ls', 'pwd', 'echo', 'cat', 'grep', 'find', 'wc', 'head', 'tail', 'npm list', 'npm outdated'];
    // const cmdStart = command.split(' ')[0];
    
    if (!allowedCommands.some(allowed => command.startsWith(allowed))) {
      return { 
        success: false, 
        error: `Command not allowed. Allowed commands: ${allowedCommands.join(', ')}` 
      };
    }
    
    const workingDir = cwd ? safePath(cwd) : PROJECT_ROOT;
    const { stdout, stderr } = await execAsync(command, { cwd: workingDir });
    
    return { 
      success: true, 
      data: {
        stdout: stdout.trim(),
        stderr: stderr.trim(),
        command,
        cwd: path.relative(PROJECT_ROOT, workingDir) || '.'
      }
    };
  } catch (error) {
    return { 
      success: false, 
      error: error instanceof Error ? error.message : 'Unknown error' 
    };
  }
}

export async function getFileTree(startPath: string = '.', maxDepth: number = 3): Promise<ToolResult> {
  try {
    const buildTree = async (dir: string, currentDepth: number): Promise<any> => {
      if (currentDepth > maxDepth) return null;
      
      const entries = await fs.readdir(dir, { withFileTypes: true });
      const tree: any = {};
      
      for (const entry of entries) {
        if (entry.name.startsWith('.') || entry.name === 'node_modules') continue;
        
        const fullPath = path.join(dir, entry.name);
        const relativePath = path.relative(PROJECT_ROOT, fullPath);
        
        if (entry.isDirectory()) {
          const subtree = await buildTree(fullPath, currentDepth + 1);
          if (subtree) {
            tree[entry.name + '/'] = subtree;
          } else {
            tree[entry.name + '/'] = '...';
          }
        } else {
          tree[entry.name] = relativePath;
        }
      }
      
      return tree;
    };
    
    const fullPath = safePath(startPath);
    const tree = await buildTree(fullPath, 0);
    
    return { success: true, data: tree };
  } catch (error) {
    return { 
      success: false, 
      error: error instanceof Error ? error.message : 'Unknown error' 
    };
  }
}