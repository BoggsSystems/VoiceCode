import React, { useEffect, useRef } from 'react';
import {
  Box,
  Typography,
  Paper,
  Chip,
  LinearProgress,
  Fade,
  Stack,
  Divider,
  IconButton,
  Tooltip
} from '@mui/material';
import {
  RecordVoiceOver,
  Mic,
  MicOff,
  Circle,
  MoreVert,
  ContentCopy,
  Clear
} from '@mui/icons-material';

interface TranscriptionSegment {
  id: string;
  text: string;
  isFinal: boolean;
  confidence: number;
  timestamp: Date;
  duration?: number;
}

interface EnhancedTranscriptionDisplayProps {
  currentPartial?: string;
  segments: TranscriptionSegment[];
  isRecording: boolean;
  isProcessing: boolean;
  showConfidence?: boolean;
  showTimestamps?: boolean;
  maxSegments?: number;
  onClear?: () => void;
  onCopy?: (text: string) => void;
}

export const EnhancedTranscriptionDisplay: React.FC<EnhancedTranscriptionDisplayProps> = ({
  currentPartial,
  segments,
  isRecording,
  isProcessing,
  showConfidence = true,
  showTimestamps = false,
  maxSegments = 50,
  onClear,
  onCopy
}) => {
  const scrollRef = useRef<HTMLDivElement>(null);
  const displaySegments = segments.slice(-maxSegments);

  useEffect(() => {
    // Auto-scroll to bottom when new content arrives
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [segments, currentPartial]);

  const getConfidenceColor = (confidence: number) => {
    if (confidence >= 0.9) return 'success';
    if (confidence >= 0.7) return 'warning';
    return 'error';
  };

  const getConfidenceLabel = (confidence: number) => {
    if (confidence >= 0.9) return 'High';
    if (confidence >= 0.7) return 'Medium';
    return 'Low';
  };

  const formatTimestamp = (date: Date) => {
    return new Date(date).toLocaleTimeString('en-US', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    });
  };

  const getAllText = () => {
    const finalText = displaySegments
      .filter(s => s.isFinal)
      .map(s => s.text)
      .join(' ');
    
    return currentPartial ? `${finalText} ${currentPartial}` : finalText;
  };

  const handleCopy = () => {
    const text = getAllText();
    if (onCopy) {
      onCopy(text);
    } else {
      navigator.clipboard.writeText(text);
    }
  };

  return (
    <Paper
      elevation={2}
      sx={{
        p: 2,
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        bgcolor: 'background.paper'
      }}
    >
      {/* Header */}
      <Box sx={{ display: 'flex', alignItems: 'center', mb: 2 }}>
        <RecordVoiceOver sx={{ mr: 1, color: 'primary.main' }} />
        <Typography variant="h6" sx={{ flex: 1 }}>
          Live Transcription
        </Typography>
        
        <Stack direction="row" spacing={1} alignItems="center">
          {isRecording && (
            <Chip
              icon={<Circle sx={{ fontSize: 12 }} />}
              label="Recording"
              size="small"
              color="error"
              sx={{
                '& .MuiChip-icon': {
                  animation: 'pulse 1.5s infinite'
                },
                '@keyframes pulse': {
                  '0%': { opacity: 1 },
                  '50%': { opacity: 0.3 },
                  '100%': { opacity: 1 }
                }
              }}
            />
          )}
          
          <Tooltip title="Copy all text">
            <IconButton size="small" onClick={handleCopy}>
              <ContentCopy fontSize="small" />
            </IconButton>
          </Tooltip>
          
          {onClear && (
            <Tooltip title="Clear transcription">
              <IconButton size="small" onClick={onClear}>
                <Clear fontSize="small" />
              </IconButton>
            </Tooltip>
          )}
        </Stack>
      </Box>

      <Divider sx={{ mb: 2 }} />

      {/* Transcription Content */}
      <Box
        ref={scrollRef}
        sx={{
          flex: 1,
          overflowY: 'auto',
          overflowX: 'hidden',
          pr: 1,
          '&::-webkit-scrollbar': {
            width: '8px',
          },
          '&::-webkit-scrollbar-track': {
            bgcolor: 'grey.100',
            borderRadius: '4px',
          },
          '&::-webkit-scrollbar-thumb': {
            bgcolor: 'grey.400',
            borderRadius: '4px',
            '&:hover': {
              bgcolor: 'grey.500',
            },
          },
        }}
      >
        <Stack spacing={1}>
          {/* Final segments */}
          {displaySegments.map((segment, index) => (
            <Fade in key={segment.id} timeout={300}>
              <Box
                sx={{
                  p: 1.5,
                  borderRadius: 1,
                  bgcolor: segment.isFinal ? 'grey.50' : 'grey.100',
                  border: '1px solid',
                  borderColor: segment.isFinal ? 'grey.200' : 'grey.300',
                  transition: 'all 0.3s ease'
                }}
              >
                <Typography
                  variant="body1"
                  sx={{
                    color: segment.isFinal ? 'text.primary' : 'text.secondary',
                    fontWeight: segment.isFinal ? 400 : 300
                  }}
                >
                  {segment.text}
                </Typography>
                
                {(showConfidence || showTimestamps) && (
                  <Box sx={{ display: 'flex', gap: 1, mt: 0.5 }}>
                    {showConfidence && segment.isFinal && (
                      <Chip
                        label={`${getConfidenceLabel(segment.confidence)} (${(segment.confidence * 100).toFixed(0)}%)`}
                        size="small"
                        color={getConfidenceColor(segment.confidence)}
                        variant="outlined"
                        sx={{ height: 20, fontSize: '0.75rem' }}
                      />
                    )}
                    
                    {showTimestamps && (
                      <Typography variant="caption" color="text.secondary">
                        {formatTimestamp(segment.timestamp)}
                      </Typography>
                    )}
                  </Box>
                )}
              </Box>
            </Fade>
          ))}

          {/* Current partial */}
          {currentPartial && (
            <Fade in timeout={200}>
              <Box
                sx={{
                  p: 1.5,
                  borderRadius: 1,
                  bgcolor: 'primary.50',
                  border: '1px solid',
                  borderColor: 'primary.200',
                  position: 'relative',
                  overflow: 'hidden'
                }}
              >
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                  <Mic sx={{ fontSize: 20, color: 'primary.main' }} />
                  <Typography
                    variant="body1"
                    sx={{
                      fontStyle: 'italic',
                      color: 'primary.700'
                    }}
                  >
                    {currentPartial}
                  </Typography>
                </Box>
                
                {/* Animated indicator */}
                <LinearProgress
                  sx={{
                    position: 'absolute',
                    bottom: 0,
                    left: 0,
                    right: 0,
                    height: 2
                  }}
                />
              </Box>
            </Fade>
          )}

          {/* Empty state */}
          {displaySegments.length === 0 && !currentPartial && (
            <Box
              sx={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                justifyContent: 'center',
                py: 8,
                color: 'text.secondary'
              }}
            >
              <MicOff sx={{ fontSize: 48, mb: 2, opacity: 0.5 }} />
              <Typography variant="body1">
                {isRecording ? 'Listening for speech...' : 'Start recording to see transcription'}
              </Typography>
            </Box>
          )}
        </Stack>
      </Box>

      {/* Footer with stats */}
      {displaySegments.length > 0 && (
        <>
          <Divider sx={{ mt: 2, mb: 1 }} />
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <Typography variant="caption" color="text.secondary">
              {displaySegments.filter(s => s.isFinal).length} segments • {getAllText().split(' ').length} words
            </Typography>
            
            {showConfidence && displaySegments.length > 0 && (
              <Typography variant="caption" color="text.secondary">
                Avg confidence: {(
                  displaySegments
                    .filter(s => s.isFinal)
                    .reduce((acc, s) => acc + s.confidence, 0) / 
                  displaySegments.filter(s => s.isFinal).length * 100
                ).toFixed(0)}%
              </Typography>
            )}
          </Box>
        </>
      )}
    </Paper>
  );
};