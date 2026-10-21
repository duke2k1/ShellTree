"""Loopback-only SSH fixture. Requires paramiko; never uses real credentials."""
import socket
import threading
import time
import paramiko

key = paramiko.RSAKey.generate(2048)

class Server(paramiko.ServerInterface):
    def __init__(self):
        self.command = None
        self.ready = threading.Event()

    def check_auth_password(self, username, password):
        if username == 'shelltree-test' and password == 'test-only-password':
            return paramiko.AUTH_SUCCESSFUL
        return paramiko.AUTH_FAILED

    def get_allowed_auths(self, username):
        return 'password'

    def check_channel_request(self, kind, chanid):
        return paramiko.OPEN_SUCCEEDED if kind == 'session' else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED

    def check_channel_exec_request(self, channel, command):
        self.command = command.decode('utf-8')
        self.ready.set()
        return True

def serve(sock):
    transport = paramiko.Transport(sock)
    try:
        transport.add_server_key(key)
        server = Server()
        transport.start_server(server=server)
        channel = transport.accept(10)
        if channel is None or not server.ready.wait(5):
            return
        command = server.command
        if 'find .' not in command or not command.startswith('sh -c '):
            channel.send_exit_status(2)
        elif '/slow' in command:
            while transport.is_active() and not channel.closed:
                time.sleep(0.1)
        elif '/denied' in command:
            channel.send_stderr(b'Permission denied\n')
            channel.send_exit_status(1)
        elif '/truncated' in command:
            channel.sendall(b'd\0.')
            channel.send_exit_status(0)
        else:
            records = [('d', '.', '4096'), ('d', './empty', '4096'), ('f', "./quote's 世界.txt", '5')]
            data = ''.join('\0'.join([kind, path, size, '1700000000.25']) + '\0' for kind, path, size in records)
            channel.sendall(data.encode('utf-8'))
            channel.send_exit_status(0)
        channel.close()
    except (EOFError, OSError, paramiko.SSHException):
        pass  # Rejection/cancellation are expected test outcomes.
    finally:
        transport.close()

with socket.socket() as listener:
    listener.bind(('127.0.0.1', 0))
    listener.listen()
    print(listener.getsockname()[1], flush=True)
    while True:
        sock, _ = listener.accept()
        threading.Thread(target=serve, args=(sock,), daemon=True).start()
