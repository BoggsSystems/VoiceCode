import { ServiceBusClient, ServiceBusSender } from '@azure/service-bus';
import { logger } from '../utils/logger.js';

export interface SdkStreamEvent {
  id: string;
  sessionId: string;
  workerId: string;
  eventType: string;
  operation: string;
  details: string;
  metadata: Record<string, any>;
  timestamp: Date;
}

export class ServiceBusPublisher {
  private client: ServiceBusClient | null = null;
  private sender: ServiceBusSender | null = null;
  private readonly queueName: string;

  constructor() {
    this.queueName = process.env.SDK_STREAM_QUEUE_NAME || 'sdk-stream-events';
    this.initialize();
  }

  private initialize(): void {
    try {
      const connectionString = process.env.AZURE_SERVICE_BUS_CONNECTION_STRING;
      
      if (!connectionString) {
        logger.warn('Service Bus connection string not configured');
        return;
      }

      this.client = new ServiceBusClient(connectionString);
      this.sender = this.client.createSender(this.queueName);
      
      logger.info({ queueName: this.queueName }, 'Service Bus publisher initialized');
    } catch (error) {
      logger.error({ error }, 'Failed to initialize Service Bus publisher');
    }
  }

  async publishEvent(event: SdkStreamEvent): Promise<void> {
    if (!this.sender) {
      logger.debug('Service Bus not configured, skipping event publish');
      return;
    }

    try {
      await this.sender.sendMessages({
        body: event,
        contentType: 'application/json',
        messageId: event.id,
        sessionId: event.sessionId
      });

      logger.debug({ eventId: event.id, eventType: event.eventType }, 'Event published to Service Bus');
    } catch (error) {
      logger.error({ error, event }, 'Failed to publish event to Service Bus');
    }
  }

  async close(): Promise<void> {
    try {
      if (this.sender) {
        await this.sender.close();
      }
      
      if (this.client) {
        await this.client.close();
      }
      
      logger.info('Service Bus publisher closed');
    } catch (error) {
      logger.error({ error }, 'Error closing Service Bus publisher');
    }
  }
}