import React from 'react';
import {
  Box,
  CircularProgress,
  Typography,
  Tooltip,
  Stack,
  Chip
} from '@mui/material';
import {
  CheckCircle,
  Warning,
  Error as ErrorIcon,
  TrendingUp,
  TrendingDown,
  TrendingFlat
} from '@mui/icons-material';

interface ConfidenceIndicatorProps {
  confidence: number;
  previousConfidence?: number;
  showTrend?: boolean;
  showLabel?: boolean;
  size?: 'small' | 'medium' | 'large';
  variant?: 'circular' | 'linear' | 'chip';
}

export const ConfidenceIndicator: React.FC<ConfidenceIndicatorProps> = ({
  confidence,
  previousConfidence,
  showTrend = true,
  showLabel = true,
  size = 'medium',
  variant = 'circular'
}) => {
  const getColor = () => {
    if (confidence >= 0.9) return 'success';
    if (confidence >= 0.7) return 'warning';
    return 'error';
  };

  const getIcon = () => {
    if (confidence >= 0.9) return <CheckCircle />;
    if (confidence >= 0.7) return <Warning />;
    return <ErrorIcon />;
  };

  const getTrendIcon = () => {
    if (!previousConfidence) return null;
    
    const diff = confidence - previousConfidence;
    if (Math.abs(diff) < 0.05) return <TrendingFlat fontSize="small" />;
    if (diff > 0) return <TrendingUp fontSize="small" color="success" />;
    return <TrendingDown fontSize="small" color="error" />;
  };

  const getSize = () => {
    switch (size) {
      case 'small': return 40;
      case 'large': return 80;
      default: return 60;
    }
  };

  const percentage = Math.round(confidence * 100);

  if (variant === 'chip') {
    return (
      <Chip
        icon={getIcon()}
        label={`${percentage}%`}
        color={getColor()}
        size={size === 'small' ? 'small' : 'medium'}
        sx={{ fontWeight: 'bold' }}
      />
    );
  }

  if (variant === 'linear') {
    return (
      <Box sx={{ width: '100%' }}>
        <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 0.5 }}>
          {getIcon()}
          <Typography variant="body2" color="text.secondary">
            Confidence
          </Typography>
          {showLabel && (
            <Typography variant="body2" fontWeight="bold" color={`${getColor()}.main`}>
              {percentage}%
            </Typography>
          )}
          {showTrend && getTrendIcon()}
        </Stack>
        <Box
          sx={{
            position: 'relative',
            height: 8,
            bgcolor: 'grey.200',
            borderRadius: 4,
            overflow: 'hidden'
          }}
        >
          <Box
            sx={{
              position: 'absolute',
              left: 0,
              top: 0,
              height: '100%',
              width: `${percentage}%`,
              bgcolor: `${getColor()}.main`,
              transition: 'width 0.3s ease',
              borderRadius: 4
            }}
          />
        </Box>
      </Box>
    );
  }

  // Circular variant (default)
  return (
    <Tooltip title={`Confidence: ${percentage}%`}>
      <Box
        sx={{
          position: 'relative',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center'
        }}
      >
        <CircularProgress
          variant="determinate"
          value={percentage}
          size={getSize()}
          thickness={4}
          color={getColor()}
          sx={{
            '& .MuiCircularProgress-circle': {
              transition: 'stroke-dashoffset 0.3s ease'
            }
          }}
        />
        <Box
          sx={{
            position: 'absolute',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center'
          }}
        >
          <Typography
            variant={size === 'small' ? 'caption' : 'body2'}
            component="div"
            color="text.secondary"
            fontWeight="bold"
          >
            {percentage}%
          </Typography>
          {showTrend && previousConfidence && size !== 'small' && (
            <Box sx={{ mt: -0.5 }}>
              {getTrendIcon()}
            </Box>
          )}
        </Box>
      </Box>
    </Tooltip>
  );
};

interface ConfidenceHistoryProps {
  history: number[];
  maxItems?: number;
  height?: number;
}

export const ConfidenceHistory: React.FC<ConfidenceHistoryProps> = ({
  history,
  maxItems = 20,
  height = 60
}) => {
  const displayHistory = history.slice(-maxItems);
  const maxValue = Math.max(...displayHistory, 1);
  
  return (
    <Box sx={{ width: '100%' }}>
      <Typography variant="caption" color="text.secondary" gutterBottom>
        Confidence History
      </Typography>
      <Box
        sx={{
          display: 'flex',
          alignItems: 'flex-end',
          height,
          gap: 0.5,
          p: 1,
          bgcolor: 'grey.50',
          borderRadius: 1,
          border: '1px solid',
          borderColor: 'grey.200'
        }}
      >
        {displayHistory.map((value, index) => {
          const color = value >= 0.9 ? 'success' : value >= 0.7 ? 'warning' : 'error';
          const heightPercent = (value / maxValue) * 100;
          
          return (
            <Box
              key={index}
              sx={{
                flex: 1,
                height: `${heightPercent}%`,
                bgcolor: `${color}.main`,
                borderRadius: 0.5,
                transition: 'height 0.3s ease',
                opacity: 0.8,
                '&:hover': {
                  opacity: 1
                }
              }}
            />
          );
        })}
      </Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', mt: 0.5 }}>
        <Typography variant="caption" color="text.secondary">
          Oldest
        </Typography>
        <Typography variant="caption" color="text.secondary">
          Current: {(displayHistory[displayHistory.length - 1] * 100).toFixed(0)}%
        </Typography>
        <Typography variant="caption" color="text.secondary">
          Newest
        </Typography>
      </Box>
    </Box>
  );
};