import React, { useState } from 'react';
import { 
  Box, 
  IconButton, 
  Typography, 
  LinearProgress, 
  Alert,
  Paper,
  Collapse,
  Switch,
  FormControlLabel
} from '@mui/material';
import { 
  Mic, 
  MicOff, 
  Stream,
  Settings as SettingsIcon
} from '@mui/icons-material';
import { useVADStreaming } from '../../../hooks/useVADStreaming';
import { useAppSelector } from '../../../store/hooks';
import { VADIndicator } from '../VADIndicator';
import { VADSettings } from '../VADSettings';
import { VADConfig } from '../../../services/vadProcessor';

export const VADStreamingRecorder: React.FC = () => {
  const [showSettings, setShowSettings] = useState(false);
  const [enableVAD, setEnableVAD] = useState(true);
  const [vadConfig, setVadConfig] = useState<Partial<VADConfig>>({});
  
  const { 
    isRecording, 
    isConnected,
    isVADActive,
    vadState,
    startRecording, 
    stopRecording, 
    error,
    audioLevel,
    vadMetrics,
    updateVADConfig
  } = useVADStreaming(enableVAD);
  
  const { partialTranscript, currentTranscript } = useAppSelector(state => state.voice);

  const handleToggleRecording = async () => {
    if (isRecording) {
      await stopRecording();
    } else {
      await startRecording();
    }
  };

  const handleVADConfigChange = (config: Partial<VADConfig>) => {
    setVadConfig(config);
    updateVADConfig(config);
  };

  const handleToggleVAD = (event: React.ChangeEvent<HTMLInputElement>) => {
    setEnableVAD(event.target.checked);
  };

  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 2,
        p: 3,
        maxWidth: 800,
        mx: 'auto'
      }}
    >
      {!isConnected && (
        <Alert severity="warning" sx={{ width: '100%', mb: 2 }}>
          Connecting to audio streaming service...
        </Alert>
      )}

      {error && (
        <Alert severity="error" sx={{ width: '100%', mb: 2 }}>
          {error}
        </Alert>
      )}

      <Paper elevation={3} sx={{ p: 3, width: '100%' }}>
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 3 }}>
          <FormControlLabel
            control={
              <Switch
                checked={enableVAD}
                onChange={handleToggleVAD}
                disabled={isRecording}
              />
            }
            label="Enable Voice Activity Detection"
          />
          <IconButton
            onClick={() => setShowSettings(!showSettings)}
            disabled={!enableVAD}
          >
            <SettingsIcon />
          </IconButton>
        </Box>

        <Box sx={{ display: 'flex', gap: 3, alignItems: 'flex-start' }}>
          {/* Recording Button and Status */}
          <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
            <Box
              sx={{
                position: 'relative',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center'
              }}
            >
              <IconButton
                onClick={handleToggleRecording}
                disabled={!isConnected}
                sx={{
                  width: 80,
                  height: 80,
                  bgcolor: isRecording ? 'error.main' : 'primary.main',
                  color: 'white',
                  '&:hover': {
                    bgcolor: isRecording ? 'error.dark' : 'primary.dark',
                  },
                  '&:disabled': {
                    bgcolor: 'action.disabledBackground',
                  },
                  transition: 'all 0.3s ease',
                  transform: isRecording ? 'scale(1.1)' : 'scale(1)',
                }}
              >
                {isRecording ? <MicOff sx={{ fontSize: 40 }} /> : <Mic sx={{ fontSize: 40 }} />}
              </IconButton>

              {isRecording && !isVADActive && (
                <Box
                  sx={{
                    position: 'absolute',
                    top: -10,
                    right: -10,
                    display: 'flex',
                    alignItems: 'center',
                    gap: 0.5,
                    bgcolor: 'error.main',
                    color: 'white',
                    px: 1,
                    py: 0.5,
                    borderRadius: 1,
                    fontSize: '0.75rem'
                  }}
                >
                  <Stream sx={{ fontSize: 16 }} />
                  <Typography variant="caption">LIVE</Typography>
                </Box>
              )}
            </Box>

            <Typography variant="body2" color="text.secondary">
              {isRecording ? 
                (enableVAD ? 'VAD Active - Speak naturally' : 'Streaming audio... Click to stop') : 
                'Click to start'
              }
            </Typography>

            {!enableVAD && isRecording && (
              <Box sx={{ width: '100%', mt: 2 }}>
                <Typography variant="caption" color="text.secondary" gutterBottom>
                  Audio Level
                </Typography>
                <LinearProgress
                  variant="determinate"
                  value={audioLevel * 100}
                  sx={{
                    height: 8,
                    borderRadius: 4,
                    bgcolor: 'grey.300',
                    '& .MuiLinearProgress-bar': {
                      bgcolor: audioLevel > 0.7 ? 'error.main' : audioLevel > 0.3 ? 'warning.main' : 'success.main',
                      transition: 'transform 0.1s ease'
                    }
                  }}
                />
              </Box>
            )}
          </Box>

          {/* VAD Indicator */}
          {enableVAD && isRecording && (
            <Box sx={{ flex: 1 }}>
              <VADIndicator
                vadState={vadState}
                isActive={isVADActive}
                audioLevel={audioLevel}
                speechSegments={vadMetrics.speechSegments}
                showDetails={false}
              />
              
              {vadMetrics.speechSegments > 0 && (
                <Box sx={{ mt: 2, p: 2, bgcolor: 'grey.50', borderRadius: 1 }}>
                  <Typography variant="caption" color="text.secondary">
                    VAD Metrics:
                  </Typography>
                  <Typography variant="body2">
                    Segments: {vadMetrics.speechSegments}
                  </Typography>
                  <Typography variant="body2">
                    Avg Duration: {(vadMetrics.averageSegmentDuration / 1000).toFixed(1)}s
                  </Typography>
                  <Typography variant="body2">
                    Total Speech: {(vadMetrics.totalSpeechDuration / 1000).toFixed(1)}s
                  </Typography>
                </Box>
              )}
            </Box>
          )}
        </Box>
      </Paper>

      {/* VAD Settings */}
      {enableVAD && (
        <Collapse in={showSettings} sx={{ width: '100%' }}>
          <VADSettings
            config={vadConfig}
            onConfigChange={handleVADConfigChange}
            compact={true}
          />
        </Collapse>
      )}

      {/* Transcription Display */}
      {(partialTranscript || currentTranscript) && (
        <Paper
          elevation={2}
          sx={{
            width: '100%',
            p: 3,
            bgcolor: 'grey.50',
            minHeight: 100
          }}
        >
          <Typography variant="caption" color="text.secondary" gutterBottom display="block">
            Transcription {enableVAD && vadState && `(${vadState})`}
          </Typography>
          <Typography 
            variant="body1" 
            sx={{ 
              fontStyle: partialTranscript && !currentTranscript ? 'italic' : 'normal',
              color: partialTranscript && !currentTranscript ? 'text.secondary' : 'text.primary'
            }}
          >
            {partialTranscript || currentTranscript}
          </Typography>
        </Paper>
      )}
    </Box>
  );
};