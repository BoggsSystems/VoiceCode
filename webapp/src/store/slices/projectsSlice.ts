import { createSlice, createAsyncThunk, PayloadAction } from '@reduxjs/toolkit';

export interface Project {
  id: string;
  name: string;
  path: string;
  language: string;
  framework?: string;
  lastAccessed: string;
  description?: string;
}

interface ProjectsState {
  projects: Project[];
  currentProject: Project | null;
  loading: boolean;
  error: string | null;
}

const initialState: ProjectsState = {
  projects: [],
  currentProject: null,
  loading: false,
  error: null,
};

// Async thunk for creating a project
export const createProject = createAsyncThunk(
  'projects/create',
  async (projectData: { name: string; description?: string; language?: string; path?: string }) => {
    // In a real app, this would make an API call
    const newProject: Project = {
      id: crypto.randomUUID(),
      name: projectData.name,
      path: projectData.path || `/projects/${projectData.name.toLowerCase().replace(/\s+/g, '-')}`,
      language: projectData.language || 'typescript',
      framework: undefined,
      lastAccessed: new Date().toISOString(),
      description: projectData.description,
    };
    
    // Simulate API delay
    await new Promise(resolve => setTimeout(resolve, 500));
    
    return newProject;
  }
);

const projectsSlice = createSlice({
  name: 'projects',
  initialState,
  reducers: {
    setProjects: (state, action: PayloadAction<Project[]>) => {
      state.projects = action.payload;
    },
    addProject: (state, action: PayloadAction<Project>) => {
      state.projects.push(action.payload);
    },
    removeProject: (state, action: PayloadAction<string>) => {
      state.projects = state.projects.filter(p => p.id !== action.payload);
    },
    setCurrentProject: (state, action: PayloadAction<Project | null>) => {
      state.currentProject = action.payload;
    },
    updateProject: (state, action: PayloadAction<Partial<Project> & { id: string }>) => {
      const index = state.projects.findIndex(p => p.id === action.payload.id);
      if (index !== -1) {
        state.projects[index] = { ...state.projects[index], ...action.payload };
      }
    },
    setLoading: (state, action: PayloadAction<boolean>) => {
      state.loading = action.payload;
    },
    setError: (state, action: PayloadAction<string | null>) => {
      state.error = action.payload;
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(createProject.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(createProject.fulfilled, (state, action) => {
        state.loading = false;
        state.projects.push(action.payload);
        state.currentProject = action.payload;
      })
      .addCase(createProject.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message || 'Failed to create project';
      });
  },
});

export const {
  setProjects,
  addProject,
  removeProject,
  setCurrentProject,
  updateProject,
  setLoading,
  setError,
} = projectsSlice.actions;

export default projectsSlice.reducer;