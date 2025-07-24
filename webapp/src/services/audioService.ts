// Direct audio service for testing without SignalR
import { apiConfig } from '../config/authConfig';

export interface TranscriptionResult {
  id: string;
  transcript: string;
  confidence: number;
  language?: string;
  timestamp: Date;
}

export class AudioService {
  private sttUrl: string;
  
  constructor() {
    this.sttUrl = apiConfig.services.stt;
  }

  async transcribeAudio(audioBlob: Blob): Promise<TranscriptionResult> {
    try {
      // Convert blob to form data
      const formData = new FormData();
      formData.append('audioFile', audioBlob, 'recording.webm');
      formData.append('language', 'en-US');

      // For testing, we'll use a mock response since auth is bypassed
      // In production, this would use proper authentication
      const mockResult: TranscriptionResult = {
        id: crypto.randomUUID(),
        transcript: "This is a test transcription. The voice service is currently in demo mode.",
        confidence: 0.95,
        language: 'en-US',
        timestamp: new Date()
      };

      // Simulate network delay
      await new Promise(resolve => setTimeout(resolve, 1000));
      
      return mockResult;

      // Actual implementation would be:
      /*
      const response = await fetch(`${this.sttUrl}/api/transcription/transcribe`, {
        method: 'POST',
        body: formData,
        headers: {
          'Authorization': `Bearer ${await this.getAccessToken()}`
        }
      });

      if (!response.ok) {
        throw new Error(`STT service error: ${response.statusText}`);
      }

      return await response.json();
      */
    } catch (error) {
      console.error('Transcription error:', error);
      throw error;
    }
  }

  async processWithClaude(transcript: string): Promise<string> {
    // Mock Claude response for testing
    const mockResponses = [
      "Here's a Python function to calculate factorial:\n\n```python\ndef factorial(n):\n    if n == 0 or n == 1:\n        return 1\n    else:\n        return n * factorial(n - 1)\n```",
      "I'll help you create a React component. Here's a simple todo list:\n\n```jsx\nfunction TodoList() {\n  const [todos, setTodos] = useState([]);\n  \n  return (\n    <div>\n      <h2>Todo List</h2>\n      {todos.map(todo => <li key={todo.id}>{todo.text}</li>)}\n    </div>\n  );\n}\n```",
      "Here's how to implement a binary search in JavaScript:\n\n```javascript\nfunction binarySearch(arr, target) {\n  let left = 0;\n  let right = arr.length - 1;\n  \n  while (left <= right) {\n    const mid = Math.floor((left + right) / 2);\n    if (arr[mid] === target) return mid;\n    if (arr[mid] < target) left = mid + 1;\n    else right = mid - 1;\n  }\n  \n  return -1;\n}\n```"
    ];

    await new Promise(resolve => setTimeout(resolve, 1500));
    return mockResponses[Math.floor(Math.random() * mockResponses.length)];
  }
}

export default new AudioService();