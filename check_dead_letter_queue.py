#!/usr/bin/env python3
"""
Check the dead letter queue for the TTS service to see if messages are failing to process.
"""

import json
from datetime import datetime
from azure.servicebus import ServiceBusClient
from azure.servicebus.management import ServiceBusAdministrationClient

# Configuration
SERVICE_BUS_CONNECTION_STRING = "Endpoint=sb://voicecode-dev-eus-sbus.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=l9ZLNdYwbOIbNC7BA7Bdr/d9c9Z/M8XbO+ASbLxDEYI="
TTS_QUEUE_NAME = "tts-processing"

def check_dead_letter_messages():
    """Check and display messages in the dead letter queue"""
    print(f"\n[{datetime.now()}] Checking dead letter queue for '{TTS_QUEUE_NAME}'...")
    
    try:
        with ServiceBusClient.from_connection_string(SERVICE_BUS_CONNECTION_STRING) as client:
            # Dead letter queue is accessed by appending /$deadletterqueue to the queue name
            receiver = client.get_queue_receiver(
                queue_name=TTS_QUEUE_NAME,
                sub_queue="deadletter",
                max_wait_time=5
            )
            
            with receiver:
                messages = receiver.receive_messages(max_message_count=10, max_wait_time=5)
                
                if messages:
                    print(f"✓ Found {len(messages)} message(s) in dead letter queue:")
                    
                    for idx, message in enumerate(messages, 1):
                        print(f"\n  Message {idx}:")
                        print(f"    - Message ID: {message.message_id}")
                        print(f"    - Enqueued Time: {message.enqueued_time_utc}")
                        print(f"    - Dead Letter Reason: {message.dead_letter_reason}")
                        print(f"    - Dead Letter Error: {message.dead_letter_error_description}")
                        
                        # Try to parse the message body
                        try:
                            body = str(message)
                            body_json = json.loads(body)
                            print(f"    - Body preview: {str(body_json)[:100]}...")
                        except:
                            print(f"    - Body (raw): {str(message)[:100]}...")
                else:
                    print("✓ No messages found in dead letter queue")
                    
    except Exception as e:
        print(f"✗ Error checking dead letter queue: {e}")

def peek_active_messages():
    """Peek at active messages without removing them"""
    print(f"\n[{datetime.now()}] Peeking at active messages in '{TTS_QUEUE_NAME}'...")
    
    try:
        with ServiceBusClient.from_connection_string(SERVICE_BUS_CONNECTION_STRING) as client:
            receiver = client.get_queue_receiver(queue_name=TTS_QUEUE_NAME)
            
            with receiver:
                messages = receiver.peek_messages(max_message_count=5)
                
                if messages:
                    print(f"✓ Found {len(messages)} active message(s):")
                    
                    for idx, message in enumerate(messages, 1):
                        print(f"\n  Message {idx}:")
                        print(f"    - Message ID: {message.message_id}")
                        print(f"    - Enqueued Time: {message.enqueued_time_utc}")
                        print(f"    - Sequence Number: {message.sequence_number}")
                        
                        # Try to parse the message body
                        try:
                            body = str(message)
                            body_json = json.loads(body)
                            print(f"    - Body: {json.dumps(body_json, indent=6)}")
                        except:
                            print(f"    - Body (raw): {str(message)}")
                else:
                    print("✓ No active messages found")
                    
    except Exception as e:
        print(f"✗ Error peeking at messages: {e}")

def main():
    """Main function"""
    print("=" * 60)
    print("Service Bus Queue Diagnostics")
    print("=" * 60)
    
    # Check queue status
    try:
        admin_client = ServiceBusAdministrationClient.from_connection_string(SERVICE_BUS_CONNECTION_STRING)
        queue_properties = admin_client.get_queue_runtime_properties(TTS_QUEUE_NAME)
        
        print(f"\nQueue '{TTS_QUEUE_NAME}' statistics:")
        print(f"  - Active messages: {queue_properties.active_message_count}")
        print(f"  - Dead letter messages: {queue_properties.dead_letter_message_count}")
        print(f"  - Scheduled messages: {queue_properties.scheduled_message_count}")
        print(f"  - Total messages: {queue_properties.total_message_count}")
        
    except Exception as e:
        print(f"✗ Error getting queue statistics: {e}")
    
    # Check dead letter queue
    check_dead_letter_messages()
    
    # Peek at active messages
    peek_active_messages()

if __name__ == "__main__":
    main()