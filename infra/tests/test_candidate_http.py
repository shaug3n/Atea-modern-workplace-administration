"""Exercise the release candidate HTTP gate against a local server."""

from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import subprocess
import threading
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "scripts" / "check-candidate-http.sh"


@contextmanager
def candidate_server(home_status=200, home_type="text/html", api_status=401):
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path == "/":
                status, content_type, body = home_status, home_type, b"<!doctype html><html><body>Atea</body></html>"
            elif self.path == "/api/platform/session":
                status, content_type, body = api_status, "application/problem+json", b'{}'
            else:
                status, content_type, body = 404, "text/plain", b"missing"
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *_args):
            pass

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield f"http://127.0.0.1:{server.server_port}"
    finally:
        server.shutdown()
        server.server_close()
        thread.join()


class CandidateHttpTests(unittest.TestCase):
    def check(self, *, home_status=200, home_type="text/html", api_status=401):
        self.assertTrue(SCRIPT.is_file(), "candidate HTTP checker must exist")
        with candidate_server(home_status, home_type, api_status) as base_url:
            return subprocess.run(["bash", str(SCRIPT), base_url], capture_output=True, text=True, check=False)

    def test_accepts_html_landing_page_and_protected_api(self):
        result = self.check()
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_rejects_landing_page_authentication_challenge(self):
        result = self.check(home_status=401, home_type="application/problem+json")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("landing page", result.stderr)

    def test_rejects_json_instead_of_html(self):
        result = self.check(home_type="application/json")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("landing page", result.stderr)

    def test_rejects_unprotected_platform_session(self):
        result = self.check(api_status=200)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("platform session", result.stderr)


if __name__ == "__main__":
    unittest.main()
