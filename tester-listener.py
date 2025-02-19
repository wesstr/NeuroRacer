import socket

# Define IP and port to listen on
UDP_IP = "0.0.0.0"  # Listen on all available network interfaces
UDP_PORT = 50000     # Must match the port used in the sender

# Create a UDP socket
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind((UDP_IP, UDP_PORT))

print(f"Listening for UDP messages on {UDP_IP}:{UDP_PORT}...")

while True:
    data, addr = sock.recvfrom(1024)  # Buffer size of 1024 bytes
    print(f"Received message from {addr}: {data.decode('utf-8')}")