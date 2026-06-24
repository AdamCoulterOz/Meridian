#!/usr/bin/env python3
"""Tiny dependency-free live-reload server for the Meridian docs site.

Serves docs/ on 127.0.0.1:8765. Into index.html it injects a small poller that
hits /__mtime once a second; when index.html's mtime changes (i.e. a rebuild),
the page reloads itself.

Usage:  python3 docs/redesigned/serve.py   (then open http://127.0.0.1:8765/)"""

import http.server
import socketserver
import os

DOCS = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
WATCH = os.path.join(DOCS, "index.html")
PORT = 8765

INJECT = b"""
<script>
(function(){
  var last=null;
  async function check(){
    try{
      var r=await fetch('/__mtime',{cache:'no-store'});
      var t=await r.text();
      if(last===null){last=t;}
      else if(t!==last){last=t;location.reload();}
    }catch(e){}
  }
  setInterval(check,1000);
})();
</script>
"""


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=DOCS, **k)

    def do_GET(self):
        path = self.path.split("?")[0]
        if path == "/__mtime":
            try:
                body = str(os.path.getmtime(WATCH)).encode()
            except OSError:
                body = b"0"
            self.send_response(200)
            self.send_header("Content-Type", "text/plain")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        if path in ("/", "/index.html"):
            try:
                with open(WATCH, "rb") as f:
                    data = f.read()
            except OSError:
                self.send_error(404)
                return
            data = data.replace(b"</body>", INJECT + b"</body>", 1) if b"</body>" in data else data + INJECT
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)
            return
        return super().do_GET()

    def log_message(self, *a):
        pass


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


if __name__ == "__main__":
    with Server(("127.0.0.1", PORT), Handler) as httpd:
        httpd.serve_forever()
