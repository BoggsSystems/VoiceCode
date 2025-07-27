import React, { useState, useEffect, useRef } from 'react';
import { useAppSelector } from '../../hooks/redux';
import { HubConnection, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { signalrDebugger } from '../../utils/signalrDebugger';
import './SignalRDebugPanel.css';

// Import version from package.json
const APP_VERSION = '1.0.4';

interface DebugEvent {
  timestamp: string;
  type: 'CONNECT' | 'DISCONNECT' | 'RECONNECTING' | 'RECONNECTED' | 'GROUP' | 'RECEIVE' | 'SEND' | 'ERROR' | 'HEARTBEAT';
  message: string;
  data?: any;
}

interface NetworkMessage {
  timestamp: string;
  direction: 'in' | 'out';
  method: string;
  data?: any;
  size?: number;
}

interface LogEntry {
  timestamp: string;
  level: 'info' | 'warn' | 'error' | 'debug';
  message: string;
  details?: any;
}

const SignalRDebugPanel: React.FC = () => {
  const [isOpen, setIsOpen] = useState(false);
  const [isPaused, setIsPaused] = useState(false);
  const [events, setEvents] = useState<DebugEvent[]>([]);
  const [networkMessages, setNetworkMessages] = useState<NetworkMessage[]>([]);
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [sendingLogs, setSendingLogs] = useState(false);
  const [logSendResult, setLogSendResult] = useState<{ success: boolean; message: string } | null>(null);
  const [connectionInfo, setConnectionInfo] = useState({
    state: 'Disconnected',
    connectionId: '',
    transport: '',
    sessionId: '',
    groups: [] as string[],
    reconnectAttempts: 0,
    lastConnected: '',
    hubUrl: '',
    error: ''
  });
  
  const connection = useAppSelector(state => state.signalr.connection);
  const sessionId = useAppSelector(state => state.signalr.sessionId);
  const signalrError = useAppSelector(state => state.signalr.error);
  const eventsEndRef = useRef<HTMLDivElement>(null);
  const logsEndRef = useRef<HTMLDivElement>(null);

  // Add log entry
  const addLog = (level: LogEntry['level'], message: string, details?: any) => {
    if (isPaused) return;
    
    const entry: LogEntry = {
      timestamp: new Date().toLocaleTimeString([], { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' }),
      level,
      message,
      details
    };
    setLogs(prev => [...prev.slice(-100), entry]);
  };

  // Auto-scroll to bottom when new events are added
  useEffect(() => {
    if (!isPaused) {
      eventsEndRef.current?.scrollIntoView({ behavior: 'smooth' });
      logsEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    }
  }, [events, networkMessages, logs, isPaused]);

  // Subscribe to global SignalR debugger logs
  useEffect(() => {
    const unsubscribe = signalrDebugger.subscribe((globalLogs) => {
      // Convert global logs to our format
      const convertedLogs: LogEntry[] = globalLogs.map(log => ({
        timestamp: new Date(log.timestamp).toLocaleTimeString([], { 
          hour12: false, 
          hour: '2-digit', 
          minute: '2-digit', 
          second: '2-digit' 
        }),
        level: log.level,
        message: log.message,
        details: log.details
      }));
      setLogs(convertedLogs);
    });

    return () => {
      unsubscribe();
    };
  }, []);

  // Log initialization info
  useEffect(() => {
    addLog('info', 'SignalR Debug Panel initialized', {
      version: APP_VERSION,
      hubUrl: process.env.REACT_APP_SIGNALR_HUB_URL,
      authToken: localStorage.getItem('auth-token') ? 'Present' : 'Missing',
      sessionId: localStorage.getItem('session-id') || 'Not set'
    });
  }, []);

  // Monitor SignalR error state
  useEffect(() => {
    if (signalrError) {
      addLog('error', 'SignalR Error', { error: signalrError });
      setConnectionInfo(prev => ({ ...prev, error: signalrError }));
    }
  }, [signalrError]);

  // Setup connection monitoring
  useEffect(() => {
    if (!connection) {
      addLog('warn', 'No SignalR connection object available');
      return;
    }

    addLog('info', 'SignalR connection object detected', {
      state: connection.state,
      connectionId: connection.connectionId
    });

    const addEvent = (type: DebugEvent['type'], message: string, data?: any) => {
      if (isPaused) return;
      
      const event: DebugEvent = {
        timestamp: new Date().toLocaleTimeString([], { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3 }),
        type,
        message,
        data
      };
      setEvents(prev => [...prev.slice(-100), event]); // Keep last 100 events
    };

    const addNetworkMessage = (direction: 'in' | 'out', method: string, data?: any) => {
      if (isPaused) return;
      
      const message: NetworkMessage = {
        timestamp: new Date().toLocaleTimeString([], { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3 }),
        direction,
        method,
        data,
        size: data ? JSON.stringify(data).length : 0
      };
      setNetworkMessages(prev => [...prev.slice(-50), message]); // Keep last 50 messages
    };

    // Monitor connection state
    const updateConnectionState = () => {
      const stateMap: { [key: number]: string } = {
        [HubConnectionState.Disconnected]: 'Disconnected',
        [HubConnectionState.Connecting]: 'Connecting',
        [HubConnectionState.Connected]: 'Connected',
        [HubConnectionState.Disconnecting]: 'Disconnecting',
        [HubConnectionState.Reconnecting]: 'Reconnecting'
      };

      setConnectionInfo(prev => ({
        ...prev,
        state: stateMap[connection.state] || 'Unknown',
        connectionId: connection.connectionId || '',
        transport: connection['connection']?.transport?.name || 'Unknown',
        sessionId: sessionId || ''
      }));
    };

    // Connection lifecycle events
    connection.onreconnecting((error) => {
      addEvent('RECONNECTING', `Reconnecting: ${error?.message || 'Connection lost'}`);
      setConnectionInfo(prev => ({ ...prev, reconnectAttempts: prev.reconnectAttempts + 1 }));
      updateConnectionState();
    });

    connection.onreconnected((connectionId) => {
      addEvent('RECONNECTED', `Reconnected with ID: ${connectionId}`);
      setConnectionInfo(prev => ({ 
        ...prev, 
        connectionId: connectionId || '',
        lastConnected: new Date().toLocaleTimeString()
      }));
      updateConnectionState();
    });

    connection.onclose((error) => {
      addEvent('DISCONNECT', `Connection closed: ${error?.message || 'No error'}`);
      updateConnectionState();
    });

    // Intercept all hub method calls
    const originalOn = connection.on.bind(connection);
    const originalInvoke = connection.invoke.bind(connection);
    const originalSend = connection.send.bind(connection);

    // Monitor incoming messages
    connection.on = (methodName: string, ...args: any[]) => {
      const callback = args[args.length - 1];
      const wrappedCallback = (...data: any[]) => {
        addEvent('RECEIVE', `${methodName}`, data);
        addNetworkMessage('in', methodName, data);
        return callback(...data);
      };
      args[args.length - 1] = wrappedCallback;
      return originalOn(methodName, ...args);
    };

    // Monitor outgoing invocations
    connection.invoke = async (methodName: string, ...args: any[]) => {
      addEvent('SEND', `Invoke: ${methodName}`, args);
      addNetworkMessage('out', methodName, args);
      try {
        const result = await originalInvoke(methodName, ...args);
        return result;
      } catch (error) {
        addEvent('ERROR', `Failed to invoke ${methodName}: ${error}`);
        throw error;
      }
    };

    // Monitor specific methods we care about
    connection.on('AudioResponseReady', (message) => {
      addEvent('RECEIVE', 'AudioResponseReady received', message);
      
      // Check for session ID mismatch
      if (message.sessionId && sessionId && message.sessionId !== sessionId) {
        addEvent('ERROR', `Session ID mismatch! Expected: ${sessionId}, Received: ${message.sessionId}`);
      }
    });

    // Initial state
    updateConnectionState();
    if (connection.state === HubConnectionState.Connected) {
      addEvent('CONNECT', `Connected with ID: ${connection.connectionId}`);
      setConnectionInfo(prev => ({ 
        ...prev, 
        lastConnected: new Date().toLocaleTimeString()
      }));
    }

    // Update state periodically
    const interval = setInterval(updateConnectionState, 1000);

    return () => {
      clearInterval(interval);
      // Restore original methods
      connection.on = originalOn;
      connection.invoke = originalInvoke;
      connection.send = originalSend;
    };
  }, [connection, sessionId, isPaused]);

  const getStateIcon = () => {
    switch (connectionInfo.state) {
      case 'Connected': return '🟢';
      case 'Connecting':
      case 'Reconnecting': return '🟡';
      case 'Disconnected':
      case 'Disconnecting': return '🔴';
      default: return '⚪';
    }
  };

  const clearLogs = () => {
    setEvents([]);
    setNetworkMessages([]);
    signalrDebugger.clear();
    setLogs([]);
    addLog('info', 'Logs cleared');
  };

  const exportDebugData = () => {
    const debugData = {
      connectionInfo,
      events,
      networkMessages,
      timestamp: new Date().toISOString()
    };
    const blob = new Blob([JSON.stringify(debugData, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `signalr-debug-${Date.now()}.json`;
    a.click();
    URL.revokeObjectURL(url);
  };

  const copySessionInfo = () => {
    const info = `Session ID: ${connectionInfo.sessionId}\nConnection ID: ${connectionInfo.connectionId}`;
    navigator.clipboard.writeText(info);
  };

  const testReconnect = () => {
    if (connection && connection.state === HubConnectionState.Connected) {
      connection.stop().then(() => connection.start());
    }
  };

  const sendLogsToBackend = async () => {
    setSendingLogs(true);
    setLogSendResult(null);
    
    try {
      // Prepare logs for sending
      const logsToSend = [...logs, ...events.map(e => ({
        timestamp: e.timestamp,
        level: e.type === 'ERROR' ? 'error' : e.type === 'RECONNECTING' ? 'warn' : 'info',
        message: `[${e.type}] ${e.message}`,
        details: e.data
      }))];

      // Get session ID and auth token
      const sessionId = localStorage.getItem('session-id') || 'no-session';
      const authToken = localStorage.getItem('auth-token');
      
      // Prepare request body
      const requestBody = {
        logs: logsToSend.map(log => ({
          timestamp: new Date().toISOString(),
          level: log.level,
          message: log.message,
          component: 'SignalRDebugPanel',
          sessionId: sessionId,
          details: {
            ...log.details,
            connectionState: connectionInfo.state,
            connectionId: connectionInfo.connectionId,
            error: connectionInfo.error
          }
        }))
      };

      // Send to backend
      const apiUrl = process.env.REACT_APP_API_BASE_URL || 'https://voicecode-router.orangewater-a2f689a8.eastus.azurecontainerapps.io';
      const response = await fetch(`${apiUrl}/api/logs/frontend/simple`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(authToken && { 'Authorization': `Bearer ${authToken}` })
        },
        body: JSON.stringify(requestBody)
      });

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`HTTP ${response.status}: ${errorText}`);
      }

      const result = await response.json();
      setLogSendResult({
        success: true,
        message: result.message || `Successfully sent ${logsToSend.length} log entries`
      });
      
      addLog('info', 'Logs sent to backend successfully', { count: logsToSend.length });
    } catch (error: any) {
      const errorMessage = error?.message || 'Unknown error';
      setLogSendResult({
        success: false,
        message: `Failed to send logs: ${errorMessage}`
      });
      
      addLog('error', 'Failed to send logs to backend', { error: errorMessage });
    } finally {
      setSendingLogs(false);
      
      // Clear result after 5 seconds
      setTimeout(() => setLogSendResult(null), 5000);
    }
  };

  if (!isOpen) {
    return (
      <button 
        className="signalr-debug-toggle"
        onClick={() => setIsOpen(true)}
        title="Open SignalR Debug Panel"
      >
        {getStateIcon()} Debug
      </button>
    );
  }

  return (
    <div className="signalr-debug-panel">
      <div className="debug-header">
        <h3>SignalR Debug Panel <span style={{fontSize: '12px', color: '#888'}}>v{APP_VERSION}</span></h3>
        <div className="debug-controls">
          <button onClick={() => setIsPaused(!isPaused)} title={isPaused ? "Resume" : "Pause"}>
            {isPaused ? '▶️' : '⏸️'}
          </button>
          <button onClick={copySessionInfo} title="Copy Session Info">📋</button>
          <button onClick={clearLogs} title="Clear Logs">🗑️</button>
          <button onClick={exportDebugData} title="Export Debug Data">💾</button>
          <button onClick={testReconnect} title="Test Reconnect">🔄</button>
          <button 
            onClick={sendLogsToBackend} 
            disabled={sendingLogs}
            title="Send Logs to Backend"
            style={{
              backgroundColor: sendingLogs ? '#666' : '#2a2a2a',
              cursor: sendingLogs ? 'not-allowed' : 'pointer'
            }}
          >
            {sendingLogs ? '📤...' : '📤'}
          </button>
          <button onClick={() => setIsOpen(false)} title="Close">✖️</button>
        </div>
      </div>

      <div className="debug-content">
        {logSendResult && (
          <div style={{
            padding: '10px',
            margin: '10px',
            backgroundColor: logSendResult.success ? '#1b5e20' : '#b71c1c',
            color: '#fff',
            borderRadius: '4px',
            fontSize: '13px',
            textAlign: 'center'
          }}>
            {logSendResult.success ? '✅' : '❌'} {logSendResult.message}
          </div>
        )}
        
        <div className="connection-status">
          <h4>Connection Status</h4>
          <div className="status-grid">
            <div className="status-item">
              <span className="status-label">Status:</span>
              <span className="status-value">{getStateIcon()} {connectionInfo.state}</span>
            </div>
            <div className="status-item">
              <span className="status-label">Connection ID:</span>
              <span className="status-value">{connectionInfo.connectionId || 'N/A'}</span>
            </div>
            <div className="status-item">
              <span className="status-label">Transport:</span>
              <span className="status-value">{connectionInfo.transport || 'N/A'}</span>
            </div>
            <div className="status-item">
              <span className="status-label">Session ID:</span>
              <span className="status-value">{connectionInfo.sessionId || 'N/A'}</span>
            </div>
            <div className="status-item">
              <span className="status-label">Groups:</span>
              <span className="status-value">{connectionInfo.groups.join(', ') || 'None'}</span>
            </div>
            <div className="status-item">
              <span className="status-label">Reconnect Attempts:</span>
              <span className="status-value">{connectionInfo.reconnectAttempts}</span>
            </div>
            <div className="status-item">
              <span className="status-label">Last Connected:</span>
              <span className="status-value">{connectionInfo.lastConnected || 'Never'}</span>
            </div>
            {connectionInfo.error && (
              <div className="status-item" style={{gridColumn: '1 / -1'}}>
                <span className="status-label" style={{color: '#f44336'}}>Error:</span>
                <span className="status-value" style={{color: '#f44336'}}>{connectionInfo.error}</span>
              </div>
            )}
          </div>
        </div>

        <div className="debug-tabs">
          <div className="debug-section">
            <h4>Events Log {isPaused && <span className="paused-indicator">(Paused)</span>}</h4>
            <div className="event-log">
              {events.map((event, index) => (
                <div key={index} className={`event-item event-${event.type.toLowerCase()}`}>
                  <span className="event-time">{event.timestamp}</span>
                  <span className="event-type">[{event.type}]</span>
                  <span className="event-message">{event.message}</span>
                  {event.data && (
                    <details className="event-data">
                      <summary>Data</summary>
                      <pre>{JSON.stringify(event.data, null, 2)}</pre>
                    </details>
                  )}
                </div>
              ))}
              <div ref={eventsEndRef} />
            </div>
          </div>

          <div className="debug-section">
            <h4>Local Logs {isPaused && <span className="paused-indicator">(Paused)</span>}</h4>
            <div className="event-log">
              {logs.map((log, index) => (
                <div key={index} className={`event-item event-${log.level}`}>
                  <span className="event-time">{log.timestamp}</span>
                  <span className="event-type" style={{
                    color: log.level === 'error' ? '#f44336' : 
                           log.level === 'warn' ? '#ff9800' : 
                           log.level === 'info' ? '#00ff00' : '#888'
                  }}>[{log.level.toUpperCase()}]</span>
                  <span className="event-message">{log.message}</span>
                  {log.details && (
                    <details className="event-data">
                      <summary>Details</summary>
                      <pre>{JSON.stringify(log.details, null, 2)}</pre>
                    </details>
                  )}
                </div>
              ))}
              <div ref={logsEndRef} />
            </div>
          </div>

          <div className="debug-section">
            <h4>Network Traffic</h4>
            <div className="network-log">
              {networkMessages.map((msg, index) => (
                <div key={index} className={`network-item network-${msg.direction}`}>
                  <span className="network-time">{msg.timestamp}</span>
                  <span className="network-direction">{msg.direction === 'in' ? '↓' : '↑'}</span>
                  <span className="network-method">{msg.method}</span>
                  {msg.size && <span className="network-size">({msg.size}B)</span>}
                  {msg.data && (
                    <details className="network-data">
                      <summary>Payload</summary>
                      <pre>{JSON.stringify(msg.data, null, 2)}</pre>
                    </details>
                  )}
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default SignalRDebugPanel;