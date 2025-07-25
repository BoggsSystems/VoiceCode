export interface StreamingMetrics {
  sessionId: string;
  startTime: Date;
  endTime?: Date;
  totalBytesStreamed: number;
  totalChunksProcessed: number;
  averageChunkSize: number;
  averageLatency: number;
  vadMetrics?: {
    speechSegments: number;
    totalSpeechDuration: number;
    averageSegmentDuration: number;
    falsePositives: number;
    missedSegments: number;
  };
  transcriptionMetrics?: {
    totalWords: number;
    totalSegments: number;
    averageConfidence: number;
    partialResultsCount: number;
    finalResultsCount: number;
    averageFinalizationDelay: number;
  };
  connectionMetrics?: {
    reconnections: number;
    connectionErrors: number;
    packetsLost: number;
    jitter: number;
  };
}

export class StreamingMetricsService {
  private metrics: Map<string, StreamingMetrics> = new Map();
  private chunkLatencies: Map<string, number[]> = new Map();
  private finalizationDelays: Map<string, number[]> = new Map();

  createSession(sessionId: string): void {
    this.metrics.set(sessionId, {
      sessionId,
      startTime: new Date(),
      totalBytesStreamed: 0,
      totalChunksProcessed: 0,
      averageChunkSize: 0,
      averageLatency: 0
    });
    this.chunkLatencies.set(sessionId, []);
    this.finalizationDelays.set(sessionId, []);
  }

  endSession(sessionId: string): void {
    const session = this.metrics.get(sessionId);
    if (session) {
      session.endTime = new Date();
    }
  }

  recordChunk(sessionId: string, chunkSize: number, latency: number): void {
    const session = this.metrics.get(sessionId);
    const latencies = this.chunkLatencies.get(sessionId);
    
    if (session && latencies) {
      session.totalBytesStreamed += chunkSize;
      session.totalChunksProcessed++;
      session.averageChunkSize = session.totalBytesStreamed / session.totalChunksProcessed;
      
      latencies.push(latency);
      session.averageLatency = latencies.reduce((a, b) => a + b, 0) / latencies.length;
    }
  }

  recordVADEvent(sessionId: string, type: 'speech' | 'falsePositive' | 'missed', duration?: number): void {
    const session = this.metrics.get(sessionId);
    if (session) {
      if (!session.vadMetrics) {
        session.vadMetrics = {
          speechSegments: 0,
          totalSpeechDuration: 0,
          averageSegmentDuration: 0,
          falsePositives: 0,
          missedSegments: 0
        };
      }
      
      switch (type) {
        case 'speech':
          session.vadMetrics.speechSegments++;
          if (duration) {
            session.vadMetrics.totalSpeechDuration += duration;
            session.vadMetrics.averageSegmentDuration = 
              session.vadMetrics.totalSpeechDuration / session.vadMetrics.speechSegments;
          }
          break;
        case 'falsePositive':
          session.vadMetrics.falsePositives++;
          break;
        case 'missed':
          session.vadMetrics.missedSegments++;
          break;
      }
    }
  }

  recordTranscriptionEvent(
    sessionId: string, 
    type: 'partial' | 'final', 
    wordCount: number, 
    confidence: number,
    finalizationDelay?: number
  ): void {
    const session = this.metrics.get(sessionId);
    const delays = this.finalizationDelays.get(sessionId);
    
    if (session) {
      if (!session.transcriptionMetrics) {
        session.transcriptionMetrics = {
          totalWords: 0,
          totalSegments: 0,
          averageConfidence: 0,
          partialResultsCount: 0,
          finalResultsCount: 0,
          averageFinalizationDelay: 0
        };
      }
      
      const metrics = session.transcriptionMetrics;
      
      if (type === 'partial') {
        metrics.partialResultsCount++;
      } else {
        metrics.finalResultsCount++;
        metrics.totalWords += wordCount;
        metrics.totalSegments++;
        
        // Update average confidence
        const totalConfidence = metrics.averageConfidence * (metrics.totalSegments - 1) + confidence;
        metrics.averageConfidence = totalConfidence / metrics.totalSegments;
        
        // Record finalization delay
        if (finalizationDelay !== undefined && delays) {
          delays.push(finalizationDelay);
          metrics.averageFinalizationDelay = 
            delays.reduce((a, b) => a + b, 0) / delays.length;
        }
      }
    }
  }

  recordConnectionEvent(sessionId: string, type: 'reconnection' | 'error' | 'packetLoss', value?: number): void {
    const session = this.metrics.get(sessionId);
    if (session) {
      if (!session.connectionMetrics) {
        session.connectionMetrics = {
          reconnections: 0,
          connectionErrors: 0,
          packetsLost: 0,
          jitter: 0
        };
      }
      
      switch (type) {
        case 'reconnection':
          session.connectionMetrics.reconnections++;
          break;
        case 'error':
          session.connectionMetrics.connectionErrors++;
          break;
        case 'packetLoss':
          if (value !== undefined) {
            session.connectionMetrics.packetsLost += value;
          }
          break;
      }
    }
  }

  getSessionMetrics(sessionId: string): StreamingMetrics | undefined {
    return this.metrics.get(sessionId);
  }

  getAllMetrics(): StreamingMetrics[] {
    return Array.from(this.metrics.values());
  }

  exportMetrics(sessionId: string): string {
    const metrics = this.metrics.get(sessionId);
    if (!metrics) return '';
    
    const duration = metrics.endTime 
      ? (metrics.endTime.getTime() - metrics.startTime.getTime()) / 1000 
      : (new Date().getTime() - metrics.startTime.getTime()) / 1000;
    
    return `
Streaming Session Metrics
========================
Session ID: ${metrics.sessionId}
Duration: ${duration.toFixed(2)}s
Start Time: ${metrics.startTime.toISOString()}
End Time: ${metrics.endTime?.toISOString() || 'Active'}

Audio Streaming
--------------
Total Bytes: ${(metrics.totalBytesStreamed / 1024).toFixed(2)} KB
Chunks Processed: ${metrics.totalChunksProcessed}
Average Chunk Size: ${metrics.averageChunkSize.toFixed(0)} bytes
Average Latency: ${metrics.averageLatency.toFixed(2)}ms
Throughput: ${((metrics.totalBytesStreamed / 1024) / duration).toFixed(2)} KB/s

${metrics.vadMetrics ? `
Voice Activity Detection
-----------------------
Speech Segments: ${metrics.vadMetrics.speechSegments}
Total Speech Duration: ${(metrics.vadMetrics.totalSpeechDuration / 1000).toFixed(2)}s
Average Segment Duration: ${(metrics.vadMetrics.averageSegmentDuration / 1000).toFixed(2)}s
False Positives: ${metrics.vadMetrics.falsePositives}
Missed Segments: ${metrics.vadMetrics.missedSegments}
` : ''}

${metrics.transcriptionMetrics ? `
Transcription
------------
Total Words: ${metrics.transcriptionMetrics.totalWords}
Total Segments: ${metrics.transcriptionMetrics.totalSegments}
Average Confidence: ${(metrics.transcriptionMetrics.averageConfidence * 100).toFixed(1)}%
Partial Results: ${metrics.transcriptionMetrics.partialResultsCount}
Final Results: ${metrics.transcriptionMetrics.finalResultsCount}
Average Finalization Delay: ${metrics.transcriptionMetrics.averageFinalizationDelay.toFixed(0)}ms
` : ''}

${metrics.connectionMetrics ? `
Connection
----------
Reconnections: ${metrics.connectionMetrics.reconnections}
Connection Errors: ${metrics.connectionMetrics.connectionErrors}
Packets Lost: ${metrics.connectionMetrics.packetsLost}
` : ''}
    `.trim();
  }

  clearMetrics(): void {
    this.metrics.clear();
    this.chunkLatencies.clear();
    this.finalizationDelays.clear();
  }
}

// Singleton instance
export const streamingMetrics = new StreamingMetricsService();