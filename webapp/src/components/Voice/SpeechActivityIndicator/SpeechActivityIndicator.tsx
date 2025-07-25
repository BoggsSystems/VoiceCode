import React, { useEffect, useState } from 'react';
import {
  Box,
  Paper,
  Typography,
  Stack,
  LinearProgress,
  Chip,
  useTheme,
  alpha
} from '@mui/material';
import {
  GraphicEq,
  MicNone,
  VolumeUp,
  RecordVoiceOver,
  SignalCellularAlt
} from '@mui/icons-material';
import { VADState } from '../../../services/vadProcessor';

interface SpeechActivityIndicatorProps {
  isActive: boolean;
  vadState?: VADState;
  audioLevel: number;
  isRecording: boolean;
  speechDuration?: number;
  variant?: 'compact' | 'detailed' | 'minimal';
  showWaveform?: boolean;
}

export const SpeechActivityIndicator: React.FC<SpeechActivityIndicatorProps> = ({
  isActive,
  vadState = VADState.Idle,
  audioLevel,
  isRecording,
  speechDuration = 0,
  variant = 'compact',
  showWaveform = true
}) => {
  const theme = useTheme();
  const [waveformData, setWaveformData] = useState<number[]>(new Array(20).fill(0));
  const [animationFrame, setAnimationFrame] = useState(0);

  // Update waveform visualization
  useEffect(() => {
    if (!isRecording || !showWaveform) return;

    const interval = setInterval(() => {
      setWaveformData(prev => {
        const newData = [...prev.slice(1)];
        // Add new value based on audio level and some randomness for visual effect
        const baseLevel = audioLevel * 100;
        const variation = (Math.random() - 0.5) * 20;
        newData.push(Math.max(0, Math.min(100, baseLevel + variation)));
        return newData;
      });
      setAnimationFrame(prev => (prev + 1) % 60);
    }, 50);

    return () => clearInterval(interval);
  }, [isRecording, audioLevel, showWaveform]);

  const getStateColor = () => {
    switch (vadState) {
      case VADState.Speech:
        return theme.palette.success.main;
      case VADState.MaybeSpeech:
      case VADState.MaybeSilence:
        return theme.palette.warning.main;
      default:
        return theme.palette.text.disabled;
    }
  };

  const getStateIcon = () => {
    switch (vadState) {
      case VADState.Speech:
        return <RecordVoiceOver />;
      case VADState.MaybeSpeech:
      case VADState.MaybeSilence:
        return <VolumeUp />;
      default:
        return <MicNone />;
    }
  };

  const getStateLabel = () => {
    switch (vadState) {
      case VADState.Speech:
        return 'Speaking';
      case VADState.MaybeSpeech:
        return 'Detecting...';
      case VADState.MaybeSilence:
        return 'Ending...';
      case VADState.Silence:
        return 'Silent';
      default:
        return 'Idle';
    }
  };

  if (variant === 'minimal') {
    return (
      <Box
        sx={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: 1,
          color: getStateColor(),
          transition: 'all 0.3s ease'
        }}
      >
        {getStateIcon()}
        {isActive && (
          <Box
            sx={{
              width: 8,
              height: 8,
              borderRadius: '50%',
              bgcolor: getStateColor(),
              animation: isActive ? 'pulse 1.5s infinite' : 'none',
              '@keyframes pulse': {
                '0%': { opacity: 1, transform: 'scale(1)' },
                '50%': { opacity: 0.6, transform: 'scale(1.2)' },
                '100%': { opacity: 1, transform: 'scale(1)' }
              }
            }}
          />
        )}
      </Box>
    );
  }

  if (variant === 'compact') {
    return (
      <Paper
        elevation={2}
        sx={{
          p: 2,
          bgcolor: alpha(theme.palette.background.paper, 0.9),
          borderLeft: `4px solid ${getStateColor()}`,
          transition: 'all 0.3s ease'
        }}
      >
        <Stack direction="row" alignItems="center" spacing={2}>
          <Box sx={{ color: getStateColor(), transition: 'color 0.3s' }}>
            {getStateIcon()}
          </Box>
          
          <Box sx={{ flex: 1 }}>
            <Typography variant="body2" color="text.secondary">
              {getStateLabel()}
            </Typography>
            {showWaveform && isRecording && (
              <Box sx={{ display: 'flex', alignItems: 'flex-end', height: 20, gap: 0.5, mt: 0.5 }}>
                {waveformData.map((value, index) => (
                  <Box
                    key={index}
                    sx={{
                      width: 3,
                      height: `${value}%`,
                      bgcolor: getStateColor(),
                      opacity: 0.8,
                      transition: 'height 0.1s ease',
                      borderRadius: 0.5
                    }}
                  />
                ))}
              </Box>
            )}
          </Box>

          {isActive && (
            <Chip
              size="small"
              label={`${(speechDuration / 1000).toFixed(1)}s`}
              color={vadState === VADState.Speech ? 'success' : 'default'}
              variant="outlined"
            />
          )}
        </Stack>
      </Paper>
    );
  }

  // Detailed variant
  return (
    <Paper
      elevation={3}
      sx={{
        p: 3,
        bgcolor: alpha(theme.palette.background.paper, 0.95),
        border: `2px solid ${getStateColor()}`,
        transition: 'all 0.3s ease'
      }}
    >
      <Stack spacing={2}>
        <Stack direction="row" alignItems="center" justifyContent="space-between">
          <Stack direction="row" alignItems="center" spacing={2}>
            <Box
              sx={{
                p: 1,
                borderRadius: 1,
                bgcolor: alpha(getStateColor(), 0.1),
                color: getStateColor(),
                transition: 'all 0.3s'
              }}
            >
              {getStateIcon()}
            </Box>
            <Box>
              <Typography variant="h6">{getStateLabel()}</Typography>
              <Typography variant="caption" color="text.secondary">
                {isRecording ? 'Recording active' : 'Recording inactive'}
              </Typography>
            </Box>
          </Stack>

          <Stack alignItems="flex-end" spacing={0.5}>
            <Chip
              size="small"
              icon={<SignalCellularAlt />}
              label={`${Math.round(audioLevel * 100)}%`}
              color={audioLevel > 0.7 ? 'error' : audioLevel > 0.3 ? 'warning' : 'default'}
              variant="outlined"
            />
            {isActive && (
              <Typography variant="caption" color="text.secondary">
                Duration: {(speechDuration / 1000).toFixed(1)}s
              </Typography>
            )}
          </Stack>
        </Stack>

        {showWaveform && (
          <Box>
            <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 1 }}>
              <GraphicEq fontSize="small" color="action" />
              <Typography variant="caption" color="text.secondary">
                Audio Activity
              </Typography>
            </Stack>
            <Box
              sx={{
                position: 'relative',
                height: 60,
                bgcolor: alpha(theme.palette.action.hover, 0.1),
                borderRadius: 1,
                overflow: 'hidden',
                p: 1
              }}
            >
              <Box
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  height: '100%',
                  gap: 0.5
                }}
              >
                {waveformData.map((value, index) => (
                  <Box
                    key={index}
                    sx={{
                      flex: 1,
                      height: `${value}%`,
                      bgcolor: getStateColor(),
                      opacity: 0.6 + (index / waveformData.length) * 0.4,
                      transition: 'all 0.1s ease',
                      borderRadius: 1,
                      minHeight: 2
                    }}
                  />
                ))}
              </Box>
            </Box>
          </Box>
        )}

        <LinearProgress
          variant="determinate"
          value={audioLevel * 100}
          sx={{
            height: 6,
            borderRadius: 3,
            bgcolor: alpha(theme.palette.action.hover, 0.1),
            '& .MuiLinearProgress-bar': {
              bgcolor: getStateColor(),
              transition: 'all 0.1s ease'
            }
          }}
        />
      </Stack>
    </Paper>
  );
};