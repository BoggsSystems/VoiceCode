import React, { useState } from 'react';
import {
  Box,
  Paper,
  Typography,
  IconButton,
  Stack,
  Divider,
  Collapse,
  Chip,
  Button,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  TextField,
  List,
  ListItem,
  ListItemText,
  ListItemSecondaryAction,
  Tooltip,
  Alert
} from '@mui/material';
import {
  History,
  ExpandMore,
  ExpandLess,
  ContentCopy,
  Delete,
  Download,
  Search,
  FilterList,
  DateRange,
  Psychology
} from '@mui/icons-material';
import { format } from 'date-fns';
import { TranscriptionSegment } from '../../../hooks/useTranscriptionSegments';

interface TranscriptionSession {
  id: string;
  startTime: Date;
  endTime?: Date;
  segments: TranscriptionSegment[];
  metadata?: {
    averageConfidence: number;
    totalWords: number;
    duration: number;
    vadEnabled: boolean;
  };
}

interface TranscriptionHistoryProps {
  sessions: TranscriptionSession[];
  onDeleteSession?: (sessionId: string) => void;
  onExportSession?: (sessionId: string) => void;
  maxSessions?: number;
}

export const TranscriptionHistory: React.FC<TranscriptionHistoryProps> = ({
  sessions,
  onDeleteSession,
  onExportSession,
  maxSessions = 10
}) => {
  const [expandedSessions, setExpandedSessions] = useState<Set<string>>(new Set());
  const [searchTerm, setSearchTerm] = useState('');
  const [showFilters, setShowFilters] = useState(false);
  const [selectedSession, setSelectedSession] = useState<TranscriptionSession | null>(null);
  const [showDetails, setShowDetails] = useState(false);

  const toggleSession = (sessionId: string) => {
    setExpandedSessions(prev => {
      const newSet = new Set(prev);
      if (newSet.has(sessionId)) {
        newSet.delete(sessionId);
      } else {
        newSet.add(sessionId);
      }
      return newSet;
    });
  };

  const handleCopyTranscript = (session: TranscriptionSession) => {
    const transcript = session.segments
      .filter(s => s.isFinal)
      .map(s => s.text)
      .join(' ');
    navigator.clipboard.writeText(transcript);
  };

  const handleExportSession = (session: TranscriptionSession) => {
    const transcript = session.segments
      .filter(s => s.isFinal)
      .map(s => `[${format(s.timestamp, 'HH:mm:ss')}] ${s.text} (${(s.confidence * 100).toFixed(0)}%)`)
      .join('\n');
    
    const metadata = `Session: ${session.id}
Start: ${format(session.startTime, 'PPpp')}
End: ${session.endTime ? format(session.endTime, 'PPpp') : 'Active'}
Total Words: ${session.metadata?.totalWords || 0}
Average Confidence: ${((session.metadata?.averageConfidence || 0) * 100).toFixed(1)}%
Duration: ${((session.metadata?.duration || 0) / 1000).toFixed(1)}s

Transcript:
${transcript}`;

    const blob = new Blob([metadata], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `transcript-${session.id}.txt`;
    a.click();
    URL.revokeObjectURL(url);
    
    onExportSession?.(session.id);
  };

  const filteredSessions = sessions
    .filter(session => {
      if (!searchTerm) return true;
      const transcript = session.segments
        .filter(s => s.isFinal)
        .map(s => s.text)
        .join(' ')
        .toLowerCase();
      return transcript.includes(searchTerm.toLowerCase());
    })
    .slice(0, maxSessions);

  const getSessionDuration = (session: TranscriptionSession) => {
    if (session.metadata?.duration) {
      return (session.metadata.duration / 1000).toFixed(1) + 's';
    }
    const endTime = session.endTime || new Date();
    const duration = endTime.getTime() - session.startTime.getTime();
    return (duration / 1000).toFixed(1) + 's';
  };

  return (
    <Box>
      <Paper elevation={2} sx={{ p: 2, mb: 2 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mb: 2 }}>
          <Stack direction="row" alignItems="center" spacing={1}>
            <History />
            <Typography variant="h6">Transcription History</Typography>
            <Chip
              size="small"
              label={`${filteredSessions.length} sessions`}
              color="primary"
              variant="outlined"
            />
          </Stack>
          
          <Stack direction="row" spacing={1}>
            <Tooltip title="Filter sessions">
              <IconButton
                size="small"
                onClick={() => setShowFilters(!showFilters)}
                color={showFilters ? 'primary' : 'default'}
              >
                <FilterList />
              </IconButton>
            </Tooltip>
          </Stack>
        </Stack>

        <Collapse in={showFilters}>
          <Stack spacing={2} sx={{ mb: 2 }}>
            <TextField
              size="small"
              placeholder="Search transcripts..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              InputProps={{
                startAdornment: <Search fontSize="small" sx={{ mr: 1, color: 'text.secondary' }} />
              }}
              fullWidth
            />
          </Stack>
        </Collapse>

        {filteredSessions.length === 0 ? (
          <Alert severity="info" variant="outlined">
            No transcription sessions found
          </Alert>
        ) : (
          <List disablePadding>
            {filteredSessions.map((session, index) => (
              <React.Fragment key={session.id}>
                {index > 0 && <Divider />}
                <ListItem
                  sx={{
                    flexDirection: 'column',
                    alignItems: 'stretch',
                    px: 0,
                    py: 1
                  }}
                >
                  <Stack direction="row" alignItems="center" justifyContent="space-between">
                    <Stack direction="row" alignItems="center" spacing={2}>
                      <IconButton
                        size="small"
                        onClick={() => toggleSession(session.id)}
                      >
                        {expandedSessions.has(session.id) ? <ExpandLess /> : <ExpandMore />}
                      </IconButton>
                      
                      <Box>
                        <Typography variant="body2">
                          {format(session.startTime, 'MMM d, yyyy h:mm a')}
                        </Typography>
                        <Stack direction="row" spacing={1} sx={{ mt: 0.5 }}>
                          <Chip
                            size="small"
                            label={`${session.metadata?.totalWords || 0} words`}
                            variant="outlined"
                          />
                          <Chip
                            size="small"
                            label={getSessionDuration(session)}
                            variant="outlined"
                          />
                          {session.metadata?.averageConfidence && (
                            <Chip
                              size="small"
                              label={`${(session.metadata.averageConfidence * 100).toFixed(0)}% conf`}
                              color={
                                session.metadata.averageConfidence >= 0.9 ? 'success' :
                                session.metadata.averageConfidence >= 0.7 ? 'warning' : 'error'
                              }
                              variant="outlined"
                            />
                          )}
                        </Stack>
                      </Box>
                    </Stack>

                    <Stack direction="row" spacing={0.5}>
                      <Tooltip title="View details">
                        <IconButton
                          size="small"
                          onClick={() => {
                            setSelectedSession(session);
                            setShowDetails(true);
                          }}
                        >
                          <Psychology />
                        </IconButton>
                      </Tooltip>
                      <Tooltip title="Copy transcript">
                        <IconButton
                          size="small"
                          onClick={() => handleCopyTranscript(session)}
                        >
                          <ContentCopy />
                        </IconButton>
                      </Tooltip>
                      <Tooltip title="Export session">
                        <IconButton
                          size="small"
                          onClick={() => handleExportSession(session)}
                        >
                          <Download />
                        </IconButton>
                      </Tooltip>
                      {onDeleteSession && (
                        <Tooltip title="Delete session">
                          <IconButton
                            size="small"
                            onClick={() => onDeleteSession(session.id)}
                            color="error"
                          >
                            <Delete />
                          </IconButton>
                        </Tooltip>
                      )}
                    </Stack>
                  </Stack>

                  <Collapse in={expandedSessions.has(session.id)}>
                    <Box sx={{ mt: 2, pl: 5, pr: 1 }}>
                      <Paper
                        variant="outlined"
                        sx={{
                          p: 2,
                          bgcolor: 'grey.50',
                          maxHeight: 200,
                          overflow: 'auto'
                        }}
                      >
                        <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                          {session.segments
                            .filter(s => s.isFinal)
                            .map(s => s.text)
                            .join(' ')}
                        </Typography>
                      </Paper>
                    </Box>
                  </Collapse>
                </ListItem>
              </React.Fragment>
            ))}
          </List>
        )}
      </Paper>

      {/* Session Details Dialog */}
      <Dialog
        open={showDetails}
        onClose={() => setShowDetails(false)}
        maxWidth="md"
        fullWidth
      >
        {selectedSession && (
          <>
            <DialogTitle>
              Session Details
              <Typography variant="caption" display="block" color="text.secondary">
                {format(selectedSession.startTime, 'PPpp')}
              </Typography>
            </DialogTitle>
            <DialogContent dividers>
              <Stack spacing={2}>
                <Box>
                  <Typography variant="subtitle2" gutterBottom>
                    Metadata
                  </Typography>
                  <Stack direction="row" spacing={2}>
                    <Chip label={`${selectedSession.metadata?.totalWords || 0} words`} />
                    <Chip label={`Duration: ${getSessionDuration(selectedSession)}`} />
                    <Chip
                      label={`Avg Confidence: ${((selectedSession.metadata?.averageConfidence || 0) * 100).toFixed(1)}%`}
                      color={
                        (selectedSession.metadata?.averageConfidence || 0) >= 0.9 ? 'success' :
                        (selectedSession.metadata?.averageConfidence || 0) >= 0.7 ? 'warning' : 'error'
                      }
                    />
                    {selectedSession.metadata?.vadEnabled && (
                      <Chip label="VAD Enabled" color="primary" variant="outlined" />
                    )}
                  </Stack>
                </Box>

                <Divider />

                <Box>
                  <Typography variant="subtitle2" gutterBottom>
                    Segments Timeline
                  </Typography>
                  <List dense sx={{ maxHeight: 300, overflow: 'auto' }}>
                    {selectedSession.segments
                      .filter(s => s.isFinal)
                      .map((segment) => (
                        <ListItem key={segment.id}>
                          <ListItemText
                            primary={segment.text}
                            secondary={
                              <Stack direction="row" spacing={1} alignItems="center">
                                <Typography variant="caption">
                                  {format(segment.timestamp, 'HH:mm:ss')}
                                </Typography>
                                <Chip
                                  size="small"
                                  label={`${(segment.confidence * 100).toFixed(0)}%`}
                                  color={
                                    segment.confidence >= 0.9 ? 'success' :
                                    segment.confidence >= 0.7 ? 'warning' : 'error'
                                  }
                                />
                              </Stack>
                            }
                          />
                        </ListItem>
                      ))}
                  </List>
                </Box>
              </Stack>
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setShowDetails(false)}>Close</Button>
              <Button
                startIcon={<Download />}
                onClick={() => {
                  handleExportSession(selectedSession);
                  setShowDetails(false);
                }}
              >
                Export
              </Button>
            </DialogActions>
          </>
        )}
      </Dialog>
    </Box>
  );
};