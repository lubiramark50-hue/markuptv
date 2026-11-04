#!/usr/bin/env python3
"""Read-only MarkUpTV API smoke checks. No writes, payment or user actions."""
import argparse
import json
import sys
import urllib.error
import urllib.parse
import urllib.request

CHECKS = (
    ("health", False),
    ("api/tv/news", True),
    ("api/search?query=test", True),
    ("api/movies/latest?page=1&pageSize=1", True),
    ("api/news?category=all&page=1&pageSize=1", True),
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", required=True, help="HTTPS origin of the deployed API")
    parser.add_argument("--timeout", type=float, default=12.0)
    args = parser.parse_args()
    base = args.base_url.rstrip("/") + "/"
    parsed = urllib.parse.urlsplit(base)
    if parsed.scheme != "https" or not parsed.netloc or parsed.username or parsed.password:
        parser.error("--base-url must be a public HTTPS origin without credentials")
    if parsed.path not in ("", "/") or parsed.query or parsed.fragment:
        parser.error("--base-url must be the API origin, not a route or query")
    failed = 0
    for route, expects_json in CHECKS:
        url = urllib.parse.urljoin(base, route)
        request = urllib.request.Request(url, headers={
            "Accept": "application/json", "User-Agent": "MarkUpTV-contract-smoke/1.0"
        })
        try:
            with urllib.request.urlopen(request, timeout=args.timeout) as response:
                payload = response.read(1024 * 1024)
                status = response.status
                if not 200 <= status < 300:
                    raise ValueError(f"unexpected HTTP {status}")
                if expects_json:
                    json.loads(payload.decode("utf-8"))
                print(f"PASS {route}: HTTP {status}")
        except (urllib.error.URLError, ValueError, UnicodeError, TimeoutError) as exc:
            print(f"FAIL {route}: {exc}", file=sys.stderr)
            failed += 1
    print(f"Result: {len(CHECKS) - failed}/{len(CHECKS)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
