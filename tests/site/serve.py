"""Serves site/ under /rexplayer/ the way GitHub Pages does, including the 404 page for missing
addresses, so browser tests see exactly what visitors see.

Usage: python tests/site/serve.py 4173
"""

from __future__ import annotations

import sys
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

SITE = Path(__file__).resolve().parents[2] / "site"
PREFIX = "/rexplayer"


class PagesHandler(SimpleHTTPRequestHandler):
    """Strips the repository prefix and answers anything missing with 404.html and a 404 status."""

    def translate_path(self, path: str) -> str:
        if path.startswith(PREFIX):
            path = path[len(PREFIX):] or "/"
        return super().translate_path(path)

    def send_error(self, code: int, message: str | None = None, explain: str | None = None) -> None:
        if code != 404:
            super().send_error(code, message, explain)
            return
        body = (SITE / "404.html").read_bytes()
        self.send_response(404)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format: str, *args: object) -> None:  # noqa: A002 - the base class names it
        """Quiet: the test runner's output is the record."""


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 4173
    server = ThreadingHTTPServer(("127.0.0.1", port), partial(PagesHandler, directory=str(SITE)))
    server.serve_forever()


if __name__ == "__main__":
    main()
