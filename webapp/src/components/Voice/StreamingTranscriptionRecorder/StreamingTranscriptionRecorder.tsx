import React, { useState, useEffect } from 'react';
import {
  Box,
  Paper,
  Grid,
  Typography,
  Switch,
  FormControlLabel,
  IconButton,
  Collapse,
  Alert,
  Divider,
  Stack,
  Tooltip,
  Card,
  CardContent
} from '@mui/material';
import {
  Mic,
  MicOff,
  Settings as SettingsIcon,
  Analytics,
  Download,
  History
} from '@mui/icons-material';
import { useVADStreaming } from '../../../hooks/useVADStreaming';
import { useTranscriptionSegments } from '../../../hooks/useTranscriptionSegments';
import { VADIndicator } from '../VADIndicator';
import { VADSettings } from '../VADSettings';
import { EnhancedTranscriptionDisplay } from '../EnhancedTranscriptionDisplay';
import { ConfidenceIndicator, ConfidenceHistory } from '../ConfidenceIndicator';
import { VADConfig } from '../../../services/vadProcessor';

export const StreamingTranscriptionRecorder: React.FC = () => {
  const [showSettings, setShowSettings] = useState(false);
  const [showMetrics, setShowMetrics] = useState(false);
  const [enableVAD, setEnableVAD] = useState(true);
  const [showConfidence, setShowConfidence] = useState(true);
  const [showTimestamps, setShowTimestamps] = useState(false);
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
  
  const {
    segments,
    currentPartial,
    confidenceHistory,
    totalWords,
    averageConfidence,
    addSegment,
    updatePartial,
    clearSegments,
    exportTranscript
  } = useTranscriptionSegments();

  // Simulate receiving transcription events (in real app, these would come from SignalR)
  useEffect(() => {
    const handlePartialTranscription = (event: CustomEvent) => {
      updatePartial(event.detail.text);
    };

    const handleFinalTranscription = (event: CustomEvent) => {
      addSegment({
        text: event.detail.text,
        isFinal: true,
        confidence: event.detail.confidence || 0.9,
        timestamp: new Date()
      });
    };

    window.addEventListener('partialTranscription', handlePartialTranscription as EventListener);
    window.addEventListener('finalTranscription', handleFinalTranscription as EventListener);

    return () => {
      window.removeEventListener('partialTranscription', handlePartialTranscription as EventListener);
      window.removeEventListener('finalTranscription', handleFinalTranscription as EventListener);
    };
  }, [updatePartial, addSegment]);

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

  const handleExportTranscript = () => {
    const transcript = exportTranscript();
    const blob = new Blob([transcript], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `transcript-${new Date().toISOString()}.txt`;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <Box sx={{ maxWidth: 1400, mx: 'auto', p: 3 }}>
      {!isConnected && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          Connecting to audio streaming service...
        </Alert>
      )}

      {error && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      <Grid container spacing={3}>
        {/* Left Column - Controls */}
        <Grid item xs={12} md={4}>
          <Stack spacing={2}>
            {/* Recording Controls */}
            <Paper elevation={3} sx={{ p: 3 }}>
              <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
                <Typography variant="h6">Recording Controls</Typography>
                <Stack direction="row" spacing={1}>
                  <Tooltip title="Settings">
                    <IconButton onClick={() => setShowSettings(!showSettings)} size="small">
                      <SettingsIcon />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title="Metrics">
                    <IconButton onClick={() => setShowMetrics(!showMetrics)} size="small">
                      <Analytics />
                    </IconButton>
                  </Tooltip>
                </Stack>
              </Box>

              <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
                <IconButton
                  onClick={handleToggleRecording}
                  disabled={!isConnected}
                  sx={{
                    width: 100,
                    height: 100,
                    bgcolor: isRecording ? 'error.main' : 'primary.main',
                    color: 'white',
                    '&:hover': {
                      bgcolor: isRecording ? 'error.dark' : 'primary.dark',
                    },
                    transition: 'all 0.3s ease',
                    transform: isRecording ? 'scale(1.1)' : 'scale(1)',
                  }}
                >
                  {isRecording ? <MicOff sx={{ fontSize: 48 }} /> : <Mic sx={{ fontSize: 48 }} />}
                </IconButton>

                <Typography variant="body1" color="text.secondary">
                  {isRecording ? 'Click to stop recording' : 'Click to start recording'}
                </Typography>

                <FormControlLabel
                  control={
                    <Switch
                      checked={enableVAD}
                      onChange={(e) => setEnableVAD(e.target.checked)}
                      disabled={isRecording}
                    />
                  }
                  label="Voice Activity Detection"
                />
              </Box>
            </Paper>

            {/* VAD Status */}
            {enableVAD && isRecording && (
              <VADIndicator
                vadState={vadState}
                isActive={isVADActive}
                audioLevel={audioLevel}
                speechSegments={vadMetrics.speechSegments}
                showDetails={false}
              />
            )}

            {/* Confidence Display */}
            {segments.length > 0 && (
              <Card>
                <CardContent>
                  <Typography variant="h6" gutterBottom>
                    Confidence Analysis
                  </Typography>
                  <Stack spacing={2}>
                    <ConfidenceIndicator
                      confidence={averageConfidence}
                      variant="linear"
                      size="medium"
                    />
                    <ConfidenceHistory history={confidenceHistory} />
                  </Stack>
                </CardContent>
              </Card>
            )}

            {/* Metrics */}
            <Collapse in={showMetrics}>
              <Card>
                <CardContent>
                  <Typography variant="h6" gutterBottom>
                    Session Metrics
                  </Typography>
                  <Stack spacing={1}>
                    <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                      <Typography variant="body2" color="text.secondary">
                        Total Words:
                      </Typography>
                      <Typography variant="body2" fontWeight="bold">
                        {totalWords}
                      </Typography>
                    </Box>
                    <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                      <Typography variant="body2" color="text.secondary">
                        Segments:
                      </Typography>
                      <Typography variant="body2" fontWeight="bold">
                        {segments.filter(s => s.isFinal).length}
                      </Typography>
                    </Box>
                    <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                      <Typography variant="body2" color="text.secondary">
                        Avg Confidence:
                      </Typography>
                      <Typography variant="body2" fontWeight="bold">
                        {(averageConfidence * 100).toFixed(1)}%
                      </Typography>
                    </Box>
                    {enableVAD && (
                      <>
                        <Divider sx={{ my: 1 }} />
                        <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                          <Typography variant="body2" color="text.secondary">
                            Speech Segments:
                          </Typography>
                          <Typography variant="body2" fontWeight="bold">
                            {vadMetrics.speechSegments}
                          </Typography>
                        </Box>
                        <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                          <Typography variant="body2" color="text.secondary">
                            Avg Duration:
                          </Typography>
                          <Typography variant="body2" fontWeight="bold">
                            {(vadMetrics.averageSegmentDuration / 1000).toFixed(1)}s
                          </Typography>
                        </Box>
                      </>
                    )}
                  </Stack>
                </CardContent>
              </Card>
            </Collapse>
          </Stack>
        </Grid>

        {/* Right Column - Transcription */}
        <Grid item xs={12} md={8}>
          <Stack spacing={2} sx={{ height: '100%' }}>
            {/* Transcription Settings */}
            <Paper elevation={2} sx={{ p: 2 }}>
              <Stack direction="row" spacing={2} alignItems="center">
                <FormControlLabel
                  control={
                    <Switch
                      checked={showConfidence}
                      onChange={(e) => setShowConfidence(e.target.checked)}
                      size="small"
                    />
                  }
                  label="Show Confidence"
                />
                <FormControlLabel
                  control={
                    <Switch
                      checked={showTimestamps}
                      onChange={(e) => setShowTimestamps(e.target.checked)}
                      size="small"
                    />
                  }
                  label="Show Timestamps"
                />
                <Box sx={{ flex: 1 }} />
                <Tooltip title="Export transcript">
                  <IconButton onClick={handleExportTranscript} disabled={segments.length === 0}>
                    <Download />
                  </IconButton>
                </Tooltip>
              </Stack>
            </Paper>

            {/* Transcription Display */}
            <Box sx={{ flex: 1, minHeight: 500 }}>
              <EnhancedTranscriptionDisplay
                currentPartial={currentPartial}
                segments={segments}
                isRecording={isRecording}
                isProcessing={false}
                showConfidence={showConfidence}
                showTimestamps={showTimestamps}
                onClear={clearSegments}
              />
            </Box>
          </Stack>
        </Grid>
      </Grid>

      {/* VAD Settings */}
      {enableVAD && (
        <Collapse in={showSettings} sx={{ mt: 3 }}>
          <VADSettings
            config={vadConfig}
            onConfigChange={handleVADConfigChange}
            compact={false}
          />
        </Collapse>
      )}
    </Box>
  );
};