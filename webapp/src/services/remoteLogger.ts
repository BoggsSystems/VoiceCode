interface LogEntry {
  timestamp: string;
  level: 'info' | 'warn' | 'error' | 'debug';
  message: string;
  data?: any;
  userAgent: string;
  url: string;
}

class RemoteLogger {
  private logs: LogEntry[] = [];
  private isEnabled: boolean = true;
  private batchSize: number = 20;
  private flushInterval: number = 2000; // 2 seconds
  private timer: NodeJS.Timeout | null = null;

  constructor() {
    // Start periodic flush
    this.startPeriodicFlush();
    
    // Flush on page unload
    window.addEventListener('beforeunload', () => {
      this.flush();
    });
  }

  private startPeriodicFlush() {
    this.timer = setInterval(() => {
      this.flush();
    }, this.flushInterval);
  }

  private createLogEntry(level: LogEntry['level'], message: string, data?: any): LogEntry {
    return {
      timestamp: new Date().toISOString(),
      level,
      message,
      data,
      userAgent: navigator.userAgent,
      url: window.location.href
    };
  }

  log(level: LogEntry['level'], message: string, data?: any) {
    if (!this.isEnabled) return;

    const entry = this.createLogEntry(level, message, data);
    this.logs.push(entry);

    // Flush if batch size reached
    if (this.logs.length >= this.batchSize) {
      this.flush();
    }
  }

  info(message: string, data?: any) {
    this.log('info', message, data);
  }

  warn(message: string, data?: any) {
    this.log('warn', message, data);
  }

  error(message: string, data?: any) {
    this.log('error', message, data);
  }

  debug(message: string, data?: any) {
    this.log('debug', message, data);
  }

  async flush() {
    if (this.logs.length === 0) return;

    const logsToSend = [...this.logs];
    this.logs = [];

    const payload = {
      logs: logsToSend,
      sessionId: sessionStorage.getItem('voiceSessionId') || 'unknown',
      device: {
        platform: navigator.platform,
        vendor: navigator.vendor,
        language: navigator.language,
        screenResolution: `${window.screen.width}x${window.screen.height}`,
        isMobile: /iPhone|iPad|iPod|Android/i.test(navigator.userAgent)
      }
    };

    try {
      // Store in localStorage for local viewing
      const existingLogs = JSON.parse(localStorage.getItem('voiceCodeLogs') || '[]');
      const allLogs = [...existingLogs, ...logsToSend].slice(-100); // Keep last 100 logs
      localStorage.setItem('voiceCodeLogs', JSON.stringify(allLogs));

      // Try to send to parent window if in iframe (for log viewer)
      if (window.parent !== window) {
        window.parent.postMessage({
          type: 'voiceCodeLogs',
          data: payload
        }, '*');
      }

      // Also try to call the log viewer's receiver function if available
      if (window.opener && window.opener.receiveLogs) {
        window.opener.receiveLogs(payload);
      }
      
      // Try to send to router service logging endpoint
      const apiUrl = process.env.REACT_APP_API_BASE_URL || 'https://voicecode-router.orangewater-a2f689a8.eastus.azurecontainerapps.io';
      const loggingEndpoint = `${apiUrl}/api/logs/frontend/simple`;
      
      // Transform logs to match FrontendLogEntry format
      const transformedPayload = {
        logs: logsToSend.map(log => ({
          timestamp: log.timestamp,
          level: log.level,
          message: log.message,
          component: 'AudioPlayer',
          sessionId: payload.sessionId,
          details: {
            data: log.data,
            url: log.url,
            userAgent: log.userAgent,
            device: payload.device
          }
        }))
      };
      
      await fetch(loggingEndpoint, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${localStorage.getItem('auth-token') || ''}`
        },
        body: JSON.stringify(transformedPayload)
      }).catch(err => {
        console.error('[RemoteLogger] Failed to send logs to backend:', err);
      });
    } catch (error) {
      console.error('[RemoteLogger] Error flushing logs:', error);
      // Still try to save to localStorage
      try {
        localStorage.setItem('voiceCodeLogsBackup', JSON.stringify(logsToSend));
      } catch (e) {}
    }
  }

  disable() {
    this.isEnabled = false;
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  enable() {
    this.isEnabled = true;
    this.startPeriodicFlush();
  }
}

// Export singleton instance
export const remoteLogger = new RemoteLogger();

// Helper function to wrap existing console methods
export function setupRemoteConsole() {
  const originalConsole = {
    log: console.log,
    warn: console.warn,
    error: console.error,
    info: console.info
  };

  console.log = (...args: any[]) => {
    originalConsole.log(...args);
    // Skip logging if it's a RemoteLog entry or Redux action
    const message = args.map(arg => 
      typeof arg === 'object' ? JSON.stringify(arg) : String(arg)
    ).join(' ');
    
    if (!message.includes('[RemoteLog]') && !message.includes('@@redux')) {
      remoteLogger.info(message);
    }
  };

  console.warn = (...args: any[]) => {
    originalConsole.warn(...args);
    const message = args.map(arg => 
      typeof arg === 'object' ? JSON.stringify(arg) : String(arg)
    ).join(' ');
    
    if (!message.includes('[RemoteLog]') && !message.includes('@@redux')) {
      remoteLogger.warn(message);
    }
  };

  console.error = (...args: any[]) => {
    originalConsole.error(...args);
    const message = args.map(arg => 
      typeof arg === 'object' ? JSON.stringify(arg) : String(arg)
    ).join(' ');
    
    if (!message.includes('[RemoteLog]') && !message.includes('@@redux')) {
      remoteLogger.error(message);
    }
  };

  console.info = (...args: any[]) => {
    originalConsole.info(...args);
    const message = args.map(arg => 
      typeof arg === 'object' ? JSON.stringify(arg) : String(arg)
    ).join(' ');
    
    if (!message.includes('[RemoteLog]') && !message.includes('@@redux')) {
      remoteLogger.info(message);
    }
  };
}