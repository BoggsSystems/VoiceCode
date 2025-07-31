import { z } from 'zod';

// Tool parameter schemas
export const ListDirSchema = z.object({
  path: z.string().describe('Directory path relative to /project root')
});

export const ReadFileSchema = z.object({
  path: z.string().describe('File path relative to /project root')
});

export const WriteFileSchema = z.object({
  path: z.string().describe('File path relative to /project root'),
  contents: z.string().describe('Contents to write to the file')
});

export const AppendFileSchema = z.object({
  path: z.string().describe('File path relative to /project root'),
  contents: z.string().describe('Contents to append to the file')
});

export const DeleteFileSchema = z.object({
  path: z.string().describe('File path relative to /project root')
});

export const SearchCodebaseSchema = z.object({
  query: z.string().describe('Search query (can use wildcards)'),
  filePattern: z.string().optional().describe('File pattern to search in (e.g., "*.ts")')
});

export const RunShellSchema = z.object({
  command: z.string().describe('Shell command to run'),
  cwd: z.string().optional().describe('Working directory relative to /project')
});

export const MoveFileSchema = z.object({
  from: z.string().describe('Source file path relative to /project'),
  to: z.string().describe('Destination file path relative to /project')
});

export const CreateDirectorySchema = z.object({
  path: z.string().describe('Directory path relative to /project')
});

export const GetFileTreeSchema = z.object({
  path: z.string().default('.').describe('Starting directory path'),
  maxDepth: z.number().default(3).describe('Maximum depth to traverse')
});

// Tool result types
export interface ToolResult {
  success: boolean;
  data?: any;
  error?: string;
}

export interface FileInfo {
  name: string;
  path: string;
  type: 'file' | 'directory';
  size?: number;
  modified?: Date;
}

export interface SearchResult {
  file: string;
  line: number;
  content: string;
  match: string;
}

export interface ProjectSummary {
  type: string;
  mainLanguages: string[];
  entryPoints: string[];
  dependencies: Record<string, string>;
  structure: {
    directories: string[];
    totalFiles: number;
    fileTypes: Record<string, number>;
  };
}