import React from 'react';
import { Box, IconButton, Typography, LinearProgress, Alert } from '@mui/material';
import { Mic, MicOff, Stream } from '@mui/icons-material';
import { useStreamingAudio } from '../../../hooks/useStreamingAudio';
import { useAppSelector } from '../../../store/hooks';

export const StreamingVoiceRecorder: React.FC = () => {
  const { 
    isRecording, 
    isConnected, 
    startRecording, 
    stopRecording, 
    error,
    audioLevel 
  } = useStreamingAudio();
  
  const { partialTranscript, currentTranscript } = useAppSelector(state => state.voice);

  const handleToggleRecording = async () => {
    if (isRecording) {
      await stopRecording();
    } else {
      await startRecording();
    }
  };

  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 2,
        p: 3,
        maxWidth: 600,
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

        {isRecording && (
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
        {isRecording ? 'Streaming audio... Click to stop' : 'Click to start streaming'}
      </Typography>

      {isRecording && (
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

      {(partialTranscript || currentTranscript) && (
        <Box
          sx={{
            width: '100%',
            mt: 3,
            p: 2,
            bgcolor: 'grey.100',
            borderRadius: 2,
            minHeight: 100
          }}
        >
          <Typography variant="caption" color="text.secondary" gutterBottom display="block">
            Transcription
          </Typography>
          <Typography variant="body1" sx={{ fontStyle: partialTranscript ? 'italic' : 'normal' }}>
            {partialTranscript || currentTranscript}
          </Typography>
        </Box>
      )}
    </Box>
  );
};