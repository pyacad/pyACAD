"""Serves the three pyACAD sites locally, linked to each other, for a look before deploying.

    python tools/preview.py            then open http://localhost:8600/

  http://localhost:8600/   site/             (pyacad.com)
  http://localhost:8601/   docs/authoring/   (docs.pyacad.com)
  http://localhost:8602/   docs/tools/       (tools.pyacad.com)

Links between the sites are written as their public addresses; this server rewrites them to
the local ports on the way out, so nothing on disk changes. Standard library only, dev-time.
Ctrl+C stops it.
"""

import functools
import http.server
import os
import socketserver
import threading

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SITES = [
    (8600, os.path.join(ROOT, 'site'), 'https://pyacad.com/'),
    (8601, os.path.join(ROOT, 'docs', 'authoring'), 'https://docs.pyacad.com/'),
    (8602, os.path.join(ROOT, 'docs', 'tools'), 'https://tools.pyacad.com/'),
]
REWRITE = [(public.encode(), ('http://localhost:%d/' % port).encode())
           for port, _folder, public in SITES]
TEXT = ('.html', '.js', '.css')


class Handler(http.server.SimpleHTTPRequestHandler):
    def send_head(self):
        path = self.translate_path(self.path)
        if os.path.isdir(path):
            path = os.path.join(path, 'index.html')
        if not path.endswith(TEXT) or not os.path.isfile(path):
            return super().send_head()
        with open(path, 'rb') as f:
            body = f.read()
        for public, local in REWRITE:
            body = body.replace(public, local)
        self.send_response(200)
        self.send_header('Content-Type', self.guess_type(path))
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        import io
        return io.BytesIO(body)

    def log_message(self, *args):
        pass


class Server(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


def main():
    servers = []
    for port, folder, public in SITES:
        server = Server(('127.0.0.1', port), functools.partial(Handler, directory=folder))
        threading.Thread(target=server.serve_forever, daemon=True).start()
        servers.append(server)
        print('%-26s http://localhost:%d/' % (public, port))
    print('Ctrl+C to stop.')
    try:
        threading.Event().wait()
    except KeyboardInterrupt:
        for server in servers:
            server.shutdown()


if __name__ == '__main__':
    main()
