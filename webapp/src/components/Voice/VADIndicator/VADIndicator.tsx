import React from 'react';
import { Box, Typography, Chip, LinearProgress, Tooltip } from '@mui/material';
import { 
  MicNone, 
  Mic, 
  MicOff, 
  GraphicEq,
  VolumeUp,
  VolumeOff
} from '@mui/icons-material';
import { VADState } from '../../../services/vadProcessor';

interface VADIndicatorProps {
  vadState: VADState | null;
  isActive: boolean;
  audioLevel: number;
  speechSegments?: number;
  showDetails?: boolean;
}

export const VADIndicator: React.FC<VADIndicatorProps> = ({
  vadState,
  isActive,
  audioLevel,
  speechSegments = 0,
  showDetails = false
}) => {
  const getStateColor = () => {
    if (!isActive) return 'default';
    
    switch (vadState) {
      case VADState.Speech:
        return 'success';
      case VADState.MaybeSpeech:
        return 'warning';
      case VADState.Silence:
      case VADState.Idle:
        return 'default';
      case VADState.MaybeSilence:
        return 'info';
      default:
        return 'default';
    }
  };

  const getStateIcon = () => {
    if (!isActive) return <MicOff />;
    
    switch (vadState) {
      case VADState.Speech:
        return <Mic />;
      case VADState.MaybeSpeech:
        return <GraphicEq />;
      case VADState.Silence:
      case VADState.Idle:
        return <MicNone />;
      case VADState.MaybeSilence:
        return <VolumeOff />;
      default:
        return <MicNone />;
    }
  };

  const getStateLabel = () => {
    if (!isActive) return 'VAD Inactive';
    
    switch (vadState) {
      case VADState.Speech:
        return 'Speaking';
      case VADState.MaybeSpeech:
        return 'Detecting...';
      case VADState.Silence:
        return 'Silent';
      case VADState.Idle:
        return 'Listening';
      case VADState.MaybeSilence:
        return 'Ending...';
      default:
        return 'Unknown';
    }
  };

  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 1,
        p: 2,
        borderRadius: 2,
        bgcolor: 'background.paper',
        boxShadow: 1
      }}
    >
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          gap: 2
        }}
      >
        <Tooltip title={getStateLabel()}>
          <Chip
            icon={getStateIcon()}
            label={getStateLabel()}
            color={getStateColor()}
            variant={vadState === VADState.Speech ? 'filled' : 'outlined'}
            sx={{
              animation: vadState === VADState.Speech ? 'pulse 1s infinite' : 'none',
              '@keyframes pulse': {
                '0%': { opacity: 1 },
                '50%': { opacity: 0.8 },
                '100%': { opacity: 1 }
              }
            }}
          />
        </Tooltip>

        {speechSegments > 0 && (
          <Typography variant="caption" color="text.secondary">
            {speechSegments} segment{speechSegments !== 1 ? 's' : ''}
          </Typography>
        )}
      </Box>

      {/* Audio Level Indicator */}
      <Box sx={{ width: '100%', maxWidth: 200 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 0.5 }}>
          <VolumeUp sx={{ fontSize: 16, color: 'text.secondary' }} />
          <Typography variant="caption" color="text.secondary">
            Audio Level
          </Typography>
        </Box>
        <LinearProgress
          variant="determinate"
          value={audioLevel * 100}
          sx={{
            height: 6,
            borderRadius: 3,
            bgcolor: 'grey.300',
            '& .MuiLinearProgress-bar': {
              borderRadius: 3,
              bgcolor: 
                audioLevel > 0.7 ? 'error.main' : 
                audioLevel > 0.3 ? 'warning.main' : 
                'success.main',
              transition: 'transform 0.1s ease'
            }
          }}
        />
      </Box>

      {showDetails && vadState && (
        <Box
          sx={{
            mt: 1,
            p: 1,
            borderRadius: 1,
            bgcolor: 'grey.100',
            width: '100%'
          }}
        >
          <Typography variant="caption" component="pre" sx={{ fontFamily: 'monospace' }}>
            {JSON.stringify({
              state: vadState,
              active: isActive,
              level: audioLevel.toFixed(3)
            }, null, 2)}
          </Typography>
        </Box>
      )}
    </Box>
  );
};