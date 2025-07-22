import React, { useEffect, useRef } from 'react';
import './AudioVisualizer.css';

interface AudioVisualizerProps {
  audioLevel: number;
  isRecording: boolean;
  isProcessing: boolean;
  width?: number;
  height?: number;
  barCount?: number;
}

const AudioVisualizer: React.FC<AudioVisualizerProps> = ({
  audioLevel,
  isRecording,
  isProcessing,
  width = 300,
  height = 80,
  barCount = 20,
}) => {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const animationFrameRef = useRef<number>();
  const barsRef = useRef<number[]>(new Array(barCount).fill(0));

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    // Set canvas size
    canvas.width = width;
    canvas.height = height;

    const barWidth = width / barCount;
    const maxBarHeight = height * 0.8;

    const updateVisualization = () => {
      if (!ctx || !canvas) return;

      // Clear canvas
      ctx.clearRect(0, 0, width, height);

      if (isProcessing) {
        // Show processing animation
        const time = Date.now() * 0.003;
        for (let i = 0; i < barCount; i++) {
          const barHeight = (Math.sin(time + i * 0.5) * 0.5 + 0.5) * maxBarHeight * 0.6;
          drawBar(ctx, i, barHeight, barWidth, maxBarHeight, '#ffc107');
        }
      } else if (isRecording) {
        // Update bars based on audio level with some randomness for visual effect
        for (let i = 0; i < barCount; i++) {
          const randomFactor = 0.7 + Math.random() * 0.3;
          const targetHeight = audioLevel * randomFactor * maxBarHeight;
          
          // Smooth transition
          barsRef.current[i] = barsRef.current[i] * 0.8 + targetHeight * 0.2;
          
          drawBar(ctx, i, barsRef.current[i], barWidth, maxBarHeight, '#dc3545');
        }
      } else {
        // Idle state - minimal bars
        for (let i = 0; i < barCount; i++) {
          barsRef.current[i] = barsRef.current[i] * 0.9;
          drawBar(ctx, i, barsRef.current[i], barWidth, maxBarHeight, '#6c757d');
        }
      }

      if (isRecording || isProcessing) {
        animationFrameRef.current = requestAnimationFrame(updateVisualization);
      }
    };

    updateVisualization();

    return () => {
      if (animationFrameRef.current) {
        cancelAnimationFrame(animationFrameRef.current);
      }
    };
  }, [audioLevel, isRecording, isProcessing, width, height, barCount]);

  const drawBar = (
    ctx: CanvasRenderingContext2D,
    index: number,
    barHeight: number,
    barWidth: number,
    maxBarHeight: number,
    color: string
  ) => {
    const x = index * barWidth;
    const y = height - barHeight;
    const actualBarWidth = barWidth * 0.7; // Add some spacing between bars

    // Create gradient
    const gradient = ctx.createLinearGradient(0, height, 0, height - maxBarHeight);
    gradient.addColorStop(0, color);
    gradient.addColorStop(1, adjustBrightness(color, 0.3));

    ctx.fillStyle = gradient;
    ctx.fillRect(x + barWidth * 0.15, y, actualBarWidth, barHeight);
  };

  const adjustBrightness = (color: string, amount: number): string => {
    // Simple color adjustment - in a real app you might want a more sophisticated approach
    const colorMap: { [key: string]: string } = {
      '#dc3545': `rgba(220, 53, 69, ${amount})`,
      '#ffc107': `rgba(255, 193, 7, ${amount})`,
      '#6c757d': `rgba(108, 117, 125, ${amount})`,
    };
    return colorMap[color] || color;
  };

  return (
    <div className="audio-visualizer">
      <canvas
        ref={canvasRef}
        className="visualizer-canvas"
        style={{ width, height }}
      />
      <div className="visualizer-info">
        {isProcessing && (
          <span className="status-text processing">
            <i className="bi bi-hourglass-split"></i>
            Processing...
          </span>
        )}
        {isRecording && (
          <span className="status-text recording">
            <i className="bi bi-mic"></i>
            Listening
          </span>
        )}
      </div>
    </div>
  );
};

export default AudioVisualizer;