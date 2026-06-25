import re
import subprocess
import threading
import time


class PingMonitor:
    def __init__(self, settings):
        self._settings = settings
        self._stopped = False
        self._thread = None
        self._fail_count = 0
        self.is_online = True
        self.last_ping_ms = -1
        self.on_status_changed = None

    @property
    def fail_count(self):
        return self._fail_count

    @property
    def should_swap(self):
        return self._fail_count >= self._settings.fails_before_swap

    def start(self):
        self._stopped = False
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()

    def stop(self):
        self._stopped = True

    def reset_fail_count(self):
        self._fail_count = 0
        self.is_online = True
        self.last_ping_ms = -1

    def _run(self):
        while not self._stopped:
            self._do_ping()
            timeout = self._settings.ping_interval_ms / 1000.0
            deadline = time.time() + timeout
            while time.time() < deadline and not self._stopped:
                time.sleep(0.05)

    def _do_ping(self):
        try:
            result = subprocess.run(
                ["ping", "-c", "1", "-W", "1", self._settings.ping_target],
                capture_output=True,
                text=True,
                timeout=2,
            )
            if result.returncode == 0:
                m = re.search(r"time=(\d+\.?\d*)\s*ms", result.stdout)
                if m:
                    self._fail_count = 0
                    self.is_online = True
                    self.last_ping_ms = float(m.group(1))
                else:
                    self._handle_fail()
            else:
                self._handle_fail()
        except Exception:
            self._handle_fail()

        if self.on_status_changed:
            self.on_status_changed(self.is_online, self.last_ping_ms)

    def _handle_fail(self):
        self._fail_count += 1
        self.last_ping_ms = -1
        offline_at = min(2, self._settings.fails_before_swap)
        if self._fail_count == offline_at:
            self.is_online = False
            from networktool.sound import play_ping_fail

            play_ping_fail()
        if self._fail_count >= self._settings.fails_before_swap:
            self.is_online = False

    def dispose(self):
        self.stop()
