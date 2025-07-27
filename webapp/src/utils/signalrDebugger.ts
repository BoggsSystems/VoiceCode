// Global SignalR debugger for logging connection attempts
export class SignalRDebugger {
  private static instance: SignalRDebugger;
  private logs: any[] = [];
  private listeners: ((logs: any[]) => void)[] = [];

  private constructor() {}

  static getInstance(): SignalRDebugger {
    if (!SignalRDebugger.instance) {
      SignalRDebugger.instance = new SignalRDebugger();
    }
    return SignalRDebugger.instance;
  }

  log(level: 'info' | 'warn' | 'error' | 'debug', message: string, details?: any) {
    const entry = {
      timestamp: new Date().toISOString(),
      level,
      message,
      details
    };
    
    this.logs.push(entry);
    console.log(`[SignalR ${level.toUpperCase()}] ${message}`, details || '');
    
    // Keep only last 200 logs
    if (this.logs.length > 200) {
      this.logs = this.logs.slice(-200);
    }
    
    // Notify listeners
    this.listeners.forEach(listener => listener([...this.logs]));
  }

  getLogs() {
    return [...this.logs];
  }

  subscribe(listener: (logs: any[]) => void) {
    this.listeners.push(listener);
    // Send current logs immediately
    listener([...this.logs]);
    
    // Return unsubscribe function
    return () => {
      const index = this.listeners.indexOf(listener);
      if (index > -1) {
        this.listeners.splice(index, 1);
      }
    };
  }

  clear() {
    this.logs = [];
    this.listeners.forEach(listener => listener([]));
  }
}

export const signalrDebugger = SignalRDebugger.getInstance();