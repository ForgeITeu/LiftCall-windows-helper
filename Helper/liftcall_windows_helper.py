#!/usr/bin/env python3
"""LiftCall desktop helper for Windows.

Local protocol, compatible with the Linux helper:
  GET  http://127.0.0.1:32145/health
  POST http://127.0.0.1:32145/show

The /show endpoint deliberately does not accept or use call data. The PWA
only signals that a new call should bring its window to the foreground.
"""

from __future__ import annotations

import argparse
import ctypes
import json
import logging
import os
import platform
import sys
from ctypes import wintypes
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any


SERVICE = "liftcall-windows-helper"
VERSION = "1.0.0"
DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 32145
DEFAULT_CONFIG = "config.json"

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

EnumWindowsProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

user32.EnumWindows.argtypes = [EnumWindowsProc, wintypes.LPARAM]
user32.EnumWindows.restype = wintypes.BOOL
user32.IsWindowVisible.argtypes = [wintypes.HWND]
user32.IsWindowVisible.restype = wintypes.BOOL
user32.IsIconic.argtypes = [wintypes.HWND]
user32.IsIconic.restype = wintypes.BOOL
user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
user32.GetWindowTextLengthW.restype = ctypes.c_int
user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetWindowTextW.restype = ctypes.c_int
user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetClassNameW.restype = ctypes.c_int
user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
user32.ShowWindow.restype = wintypes.BOOL
user32.BringWindowToTop.argtypes = [wintypes.HWND]
user32.BringWindowToTop.restype = wintypes.BOOL
user32.SetForegroundWindow.argtypes = [wintypes.HWND]
user32.SetForegroundWindow.restype = wintypes.BOOL
user32.SetFocus.argtypes = [wintypes.HWND]
user32.SetFocus.restype = wintypes.HWND
user32.GetForegroundWindow.argtypes = []
user32.GetForegroundWindow.restype = wintypes.HWND
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
user32.GetWindowThreadProcessId.restype = wintypes.DWORD
user32.AttachThreadInput.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.BOOL]
user32.AttachThreadInput.restype = wintypes.BOOL
kernel32.GetCurrentThreadId.argtypes = []
kernel32.GetCurrentThreadId.restype = wintypes.DWORD

SW_RESTORE = 9
SW_SHOW = 5
SW_MAXIMIZE = 3


def text_of_window(hwnd: int) -> str:
    length = user32.GetWindowTextLengthW(hwnd)
    if length <= 0:
        return ""
    buffer = ctypes.create_unicode_buffer(length + 1)
    user32.GetWindowTextW(hwnd, buffer, length + 1)
    return buffer.value


def class_of_window(hwnd: int) -> str:
    buffer = ctypes.create_unicode_buffer(256)
    user32.GetClassNameW(hwnd, buffer, len(buffer))
    return buffer.value


def process_id_of_window(hwnd: int) -> int:
    process_id = wintypes.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(process_id))
    return int(process_id.value)


def process_name(process_id: int) -> str:
    # Avoid pywin32/psutil. This is best-effort and title/class matching remains
    # fully functional if process inspection is unavailable.
    try:
        import subprocess

        result = subprocess.run(
            ["tasklist", "/FI", f"PID eq {process_id}", "/FO", "CSV", "/NH"],
            capture_output=True,
            text=True,
            timeout=1,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            check=False,
        )
        line = result.stdout.strip().splitlines()[0] if result.stdout.strip() else ""
        return line.split(",", 1)[0].strip('"') if line else ""
    except (OSError, subprocess.SubprocessError, IndexError):
        return ""


def normalise(value: str) -> str:
    return value.casefold().strip()


def window_matches(hwnd: int, config: dict[str, Any]) -> bool:
    if not user32.IsWindowVisible(hwnd):
        return False

    title = text_of_window(hwnd)
    window_class = class_of_window(hwnd)
    pid = process_id_of_window(hwnd)
    executable = process_name(pid)

    title_contains = normalise(str(config.get("window_title_contains", "")))
    expected_class = normalise(str(config.get("window_class", "")))
    expected_process = normalise(str(config.get("process_name", "")))

    checks = []
    if title_contains:
        checks.append(title_contains in normalise(title))
    if expected_class:
        checks.append(normalise(window_class) == expected_class)
    if expected_process:
        checks.append(normalise(executable) == expected_process)

    # At least one selector is required, and all configured selectors must
    # match. This prevents accidentally foregrounding an unrelated window.
    return bool(checks) and all(checks)


def find_window(config: dict[str, Any]) -> tuple[int | None, dict[str, str]]:
    matches: list[tuple[int, dict[str, str]]] = []

    @EnumWindowsProc
    def callback(hwnd: int, _lparam: int) -> bool:
        if window_matches(hwnd, config):
            matches.append(
                (
                    hwnd,
                    {
                        "title": text_of_window(hwnd),
                        "class": class_of_window(hwnd),
                        "process": process_name(process_id_of_window(hwnd)),
                    },
                )
            )
        return True

    if not user32.EnumWindows(callback, 0):
        return None, {}
    return matches[0] if matches else (None, {})


def activate_window(hwnd: int, maximize: bool = True) -> bool:
    if user32.IsIconic(hwnd):
        user32.ShowWindow(hwnd, SW_RESTORE)
    else:
        user32.ShowWindow(hwnd, SW_SHOW)

    foreground = user32.GetForegroundWindow()
    current_thread = kernel32.GetCurrentThreadId()
    foreground_thread = user32.GetWindowThreadProcessId(foreground, None) if foreground else 0
    target_thread = user32.GetWindowThreadProcessId(hwnd, None)
    attached: list[tuple[int, int]] = []

    try:
        for source, target in ((foreground_thread, current_thread), (target_thread, current_thread)):
            if source and source != target and user32.AttachThreadInput(source, target, True):
                attached.append((source, target))
        user32.BringWindowToTop(hwnd)
        user32.SetForegroundWindow(hwnd)
        user32.SetFocus(hwnd)
        if maximize:
            user32.ShowWindow(hwnd, SW_MAXIMIZE)
        return user32.GetForegroundWindow() == hwnd
    finally:
        for source, target in reversed(attached):
            user32.AttachThreadInput(source, target, False)


def load_config(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {
            "host": DEFAULT_HOST,
            "port": DEFAULT_PORT,
            "window_title_contains": "LiftCall",
            "window_class": "",
            "process_name": "",
            "maximize": True,
        }
    with path.open("r", encoding="utf-8") as handle:
        config = json.load(handle)
    if not isinstance(config, dict):
        raise ValueError("config.json musi zawierać obiekt JSON")
    return config


class Handler(BaseHTTPRequestHandler):
    server_version = SERVICE
    protocol_version = "HTTP/1.1"

    def _json(self, status: HTTPStatus, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:  # noqa: N802
        if self.path != "/health":
            self._json(HTTPStatus.NOT_FOUND, {"error": "not_found"})
            return
        hwnd, window = find_window(self.server.helper_config)
        self._json(
            HTTPStatus.OK,
            {
                "service": SERVICE,
                "version": VERSION,
                "platform": "windows",
                "status": "ok",
                "window_found": hwnd is not None,
                "window": window,
            },
        )

    def do_POST(self) -> None:  # noqa: N802
        if self.path != "/show":
            self._json(HTTPStatus.NOT_FOUND, {"error": "not_found"})
            return

        content_length = int(self.headers.get("Content-Length", "0") or "0")
        if content_length > 64 * 1024:
            self._json(HTTPStatus.REQUEST_ENTITY_TOO_LARGE, {"error": "body_too_large"})
            return
        if content_length:
            self.rfile.read(content_length)

        hwnd, window = find_window(self.server.helper_config)
        if hwnd is None:
            self._json(HTTPStatus.NOT_FOUND, {"status": "window_not_found"})
            return

        activated = activate_window(hwnd, bool(self.server.helper_config.get("maximize", True)))
        self._json(
            HTTPStatus.OK if activated else HTTPStatus.INTERNAL_SERVER_ERROR,
            {"status": "shown" if activated else "activation_failed", "window": window},
        )

    def log_message(self, format: str, *args: Any) -> None:
        logging.info("%s - %s", self.address_string(), format % args)


class Server(ThreadingHTTPServer):
    allow_reuse_address = True
    daemon_threads = True

    def __init__(self, address: tuple[str, int], config: dict[str, Any]):
        super().__init__(address, Handler)
        self.helper_config = config


def main() -> int:
    parser = argparse.ArgumentParser(description="LiftCall Windows desktop helper")
    parser.add_argument("--config", default=DEFAULT_CONFIG, help="ścieżka do config.json")
    parser.add_argument("--verbose", action="store_true", help="włącz logowanie żądań")
    args = parser.parse_args()

    if os.name != "nt":
        print("Ten helper jest przeznaczony dla Windows.", file=sys.stderr)
        return 2

    logging.basicConfig(
        level=logging.INFO if args.verbose else logging.WARNING,
        format="%(asctime)s %(levelname)s %(message)s",
    )
    config = load_config(Path(args.config).expanduser())
    host = str(config.get("host", DEFAULT_HOST))
    port = int(config.get("port", DEFAULT_PORT))
    if host not in {"127.0.0.1", "localhost"}:
        raise ValueError("helper może nasłuchiwać wyłącznie na localhost")

    server = Server((DEFAULT_HOST, port), config)
    logging.warning("%s listening on http://%s:%d", SERVICE, DEFAULT_HOST, port)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
