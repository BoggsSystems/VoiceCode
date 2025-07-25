import { useState, useCallback, useEffect, useRef } from 'react';
import { useAppSelector } from '../store/hooks';

export interface TranscriptionSegment {
  id: string;
  text: string;
  isFinal: boolean;
  confidence: number;
  timestamp: Date;
  duration?: number;
}

interface UseTranscriptionSegmentsResult {
  segments: TranscriptionSegment[];
  currentPartial: string | null;
  confidenceHistory: number[];
  totalWords: number;
  averageConfidence: number;
  addSegment: (segment: Omit<TranscriptionSegment, 'id'>) => void;
  updatePartial: (text: string) => void;
  clearSegments: () => void;
  exportTranscript: () => string;
}

export const useTranscriptionSegments = (maxSegments: number = 100): UseTranscriptionSegmentsResult => {
  const [segments, setSegments] = useState<TranscriptionSegment[]>([]);
  const [currentPartial, setCurrentPartial] = useState<string | null>(null);
  const [confidenceHistory, setConfidenceHistory] = useState<number[]>([]);
  const partialIdRef = useRef<string | null>(null);
  
  // Get transcription state from Redux
  const { currentTranscript, partialTranscript } = useAppSelector(state => state.voice);

  // Handle Redux state changes
  useEffect(() => {
    if (partialTranscript && partialTranscript !== currentPartial) {
      updatePartial(partialTranscript);
    }
  }, [partialTranscript]);

  useEffect(() => {
    if (currentTranscript && currentPartial) {
      // Convert partial to final
      finalizePartial(currentTranscript);
    }
  }, [currentTranscript]);

  const addSegment = useCallback((segment: Omit<TranscriptionSegment, 'id'>) => {
    const newSegment: TranscriptionSegment = {
      ...segment,
      id: Date.now().toString()
    };

    setSegments(prev => {
      const updated = [...prev, newSegment];
      // Keep only the last maxSegments
      return updated.slice(-maxSegments);
    });

    // Update confidence history for final segments
    if (segment.isFinal) {
      setConfidenceHistory(prev => [...prev, segment.confidence].slice(-50));
    }
  }, [maxSegments]);

  const updatePartial = useCallback((text: string) => {
    if (!text) {
      setCurrentPartial(null);
      partialIdRef.current = null;
      return;
    }

    // Create or update partial segment
    if (!partialIdRef.current) {
      partialIdRef.current = `partial-${Date.now()}`;
    }

    setCurrentPartial(text);
  }, []);

  const finalizePartial = useCallback((finalText: string) => {
    if (partialIdRef.current && currentPartial) {
      // Add as final segment
      addSegment({
        text: finalText,
        isFinal: true,
        confidence: 0.9, // Default confidence for finalized partials
        timestamp: new Date()
      });

      // Clear partial
      setCurrentPartial(null);
      partialIdRef.current = null;
    }
  }, [currentPartial, addSegment]);

  const clearSegments = useCallback(() => {
    setSegments([]);
    setCurrentPartial(null);
    setConfidenceHistory([]);
    partialIdRef.current = null;
  }, []);

  const exportTranscript = useCallback(() => {
    const finalSegments = segments.filter(s => s.isFinal);
    const transcript = finalSegments.map(s => s.text).join(' ');
    return currentPartial ? `${transcript} ${currentPartial}` : transcript;
  }, [segments, currentPartial]);

  // Calculate metrics
  const totalWords = segments
    .filter(s => s.isFinal)
    .reduce((count, segment) => count + segment.text.split(/\s+/).length, 0);

  const averageConfidence = segments.filter(s => s.isFinal).length > 0
    ? segments
        .filter(s => s.isFinal)
        .reduce((sum, segment) => sum + segment.confidence, 0) / segments.filter(s => s.isFinal).length
    : 0;

  return {
    segments,
    currentPartial,
    confidenceHistory,
    totalWords,
    averageConfidence,
    addSegment,
    updatePartial,
    clearSegments,
    exportTranscript
  };
};

// Hook for managing transcription history across sessions
export const useTranscriptionHistory = () => {
  const [history, setHistory] = useState<TranscriptionSegment[][]>([]);
  
  const saveCurrentSession = useCallback((segments: TranscriptionSegment[]) => {
    if (segments.length > 0) {
      setHistory(prev => [...prev, segments]);
    }
  }, []);

  const clearHistory = useCallback(() => {
    setHistory([]);
  }, []);

  const exportFullHistory = useCallback(() => {
    return history.map((session, index) => {
      const transcript = session
        .filter(s => s.isFinal)
        .map(s => s.text)
        .join(' ');
      return `Session ${index + 1}:\n${transcript}`;
    }).join('\n\n');
  }, [history]);

  return {
    history,
    saveCurrentSession,
    clearHistory,
    exportFullHistory
  };
};