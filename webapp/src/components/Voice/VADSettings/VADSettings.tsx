import React, { useState } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Slider,
  Switch,
  FormControlLabel,
  Button,
  Divider,
  Tooltip,
  IconButton,
  Collapse
} from '@mui/material';
import {
  Settings,
  RestartAlt,
  ExpandMore,
  ExpandLess,
  Info
} from '@mui/icons-material';
import { VADConfig } from '../../../services/vadProcessor';

interface VADSettingsProps {
  config: Partial<VADConfig>;
  onConfigChange: (config: Partial<VADConfig>) => void;
  onReset?: () => void;
  compact?: boolean;
}

const defaultConfig: VADConfig = {
  energyThreshold: 0.01,
  energyHistorySize: 50,
  speechThreshold: 0.7,
  silenceThreshold: 0.3,
  minSpeechDuration: 300,
  maxSilenceDuration: 1500,
  leadingBuffer: 300,
  trailingBuffer: 500,
  frameDuration: 20,
  sampleRate: 16000
};

export const VADSettings: React.FC<VADSettingsProps> = ({
  config,
  onConfigChange,
  onReset,
  compact = false
}) => {
  const [expanded, setExpanded] = useState(!compact);
  const currentConfig = { ...defaultConfig, ...config };

  const handleSliderChange = (key: keyof VADConfig) => (
    event: Event,
    value: number | number[]
  ) => {
    onConfigChange({ [key]: value as number });
  };

  const handleReset = () => {
    onConfigChange(defaultConfig);
    onReset?.();
  };

  const content = (
    <>
      <Box sx={{ mb: 3 }}>
        <Typography variant="subtitle2" gutterBottom>
          Detection Sensitivity
        </Typography>
        
        <Box sx={{ px: 2 }}>
          <Box sx={{ mb: 2 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography variant="body2">Speech Threshold</Typography>
              <Tooltip title="Higher values require stronger speech signal">
                <Info sx={{ fontSize: 16, color: 'text.secondary' }} />
              </Tooltip>
            </Box>
            <Slider
              value={currentConfig.speechThreshold}
              onChange={handleSliderChange('speechThreshold')}
              min={0.5}
              max={0.95}
              step={0.05}
              marks
              valueLabelDisplay="auto"
              valueLabelFormat={(value) => `${(value * 100).toFixed(0)}%`}
            />
          </Box>

          <Box sx={{ mb: 2 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography variant="body2">Silence Threshold</Typography>
              <Tooltip title="Lower values require quieter signal for silence">
                <Info sx={{ fontSize: 16, color: 'text.secondary' }} />
              </Tooltip>
            </Box>
            <Slider
              value={currentConfig.silenceThreshold}
              onChange={handleSliderChange('silenceThreshold')}
              min={0.1}
              max={0.5}
              step={0.05}
              marks
              valueLabelDisplay="auto"
              valueLabelFormat={(value) => `${(value * 100).toFixed(0)}%`}
            />
          </Box>

          <Box sx={{ mb: 2 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography variant="body2">Energy Threshold</Typography>
              <Tooltip title="Minimum audio energy to trigger detection">
                <Info sx={{ fontSize: 16, color: 'text.secondary' }} />
              </Tooltip>
            </Box>
            <Slider
              value={currentConfig.energyThreshold}
              onChange={handleSliderChange('energyThreshold')}
              min={0.001}
              max={0.05}
              step={0.001}
              marks
              valueLabelDisplay="auto"
              valueLabelFormat={(value) => value.toFixed(3)}
            />
          </Box>
        </Box>
      </Box>

      <Divider sx={{ my: 2 }} />

      <Box sx={{ mb: 3 }}>
        <Typography variant="subtitle2" gutterBottom>
          Timing Parameters
        </Typography>
        
        <Box sx={{ px: 2 }}>
          <Box sx={{ mb: 2 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography variant="body2">Min Speech Duration (ms)</Typography>
              <Tooltip title="Minimum duration to consider as speech">
                <Info sx={{ fontSize: 16, color: 'text.secondary' }} />
              </Tooltip>
            </Box>
            <Slider
              value={currentConfig.minSpeechDuration}
              onChange={handleSliderChange('minSpeechDuration')}
              min={100}
              max={1000}
              step={50}
              marks
              valueLabelDisplay="auto"
            />
          </Box>

          <Box sx={{ mb: 2 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography variant="body2">Max Silence Duration (ms)</Typography>
              <Tooltip title="Maximum silence before ending speech">
                <Info sx={{ fontSize: 16, color: 'text.secondary' }} />
              </Tooltip>
            </Box>
            <Slider
              value={currentConfig.maxSilenceDuration}
              onChange={handleSliderChange('maxSilenceDuration')}
              min={500}
              max={3000}
              step={100}
              marks
              valueLabelDisplay="auto"
            />
          </Box>
        </Box>
      </Box>

      <Divider sx={{ my: 2 }} />

      <Box sx={{ mb: 3 }}>
        <Typography variant="subtitle2" gutterBottom>
          Buffer Settings
        </Typography>
        
        <Box sx={{ px: 2 }}>
          <Box sx={{ mb: 2 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography variant="body2">Leading Buffer (ms)</Typography>
              <Tooltip title="Audio to capture before speech starts">
                <Info sx={{ fontSize: 16, color: 'text.secondary' }} />
              </Tooltip>
            </Box>
            <Slider
              value={currentConfig.leadingBuffer}
              onChange={handleSliderChange('leadingBuffer')}
              min={0}
              max={1000}
              step={50}
              marks
              valueLabelDisplay="auto"
            />
          </Box>

          <Box sx={{ mb: 2 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography variant="body2">Trailing Buffer (ms)</Typography>
              <Tooltip title="Audio to capture after speech ends">
                <Info sx={{ fontSize: 16, color: 'text.secondary' }} />
              </Tooltip>
            </Box>
            <Slider
              value={currentConfig.trailingBuffer}
              onChange={handleSliderChange('trailingBuffer')}
              min={0}
              max={1000}
              step={50}
              marks
              valueLabelDisplay="auto"
            />
          </Box>
        </Box>
      </Box>

      <Box sx={{ display: 'flex', justifyContent: 'center', mt: 3 }}>
        <Button
          variant="outlined"
          startIcon={<RestartAlt />}
          onClick={handleReset}
          size="small"
        >
          Reset to Defaults
        </Button>
      </Box>
    </>
  );

  if (compact) {
    return (
      <Card>
        <CardContent>
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Settings />
              <Typography variant="h6">VAD Settings</Typography>
            </Box>
            <IconButton onClick={() => setExpanded(!expanded)} size="small">
              {expanded ? <ExpandLess /> : <ExpandMore />}
            </IconButton>
          </Box>
          <Collapse in={expanded}>
            <Box sx={{ mt: 2 }}>
              {content}
            </Box>
          </Collapse>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardContent>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 2 }}>
          <Settings />
          <Typography variant="h6">VAD Settings</Typography>
        </Box>
        {content}
      </CardContent>
    </Card>
  );
};