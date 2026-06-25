import threading
import time

import psutil


class TrafficStats:
    def __init__(self, down_bytes=0, up_bytes=0, down_pkts=0, up_pkts=0):
        self.down_bytes_per_sec = down_bytes
        self.up_bytes_per_sec = up_bytes
        self.down_pkts = down_pkts
        self.up_pkts = up_pkts


class TrafficMonitor:
    def __init__(self):
        self._stopped = False
        self._thread = None
        self._prev_bytes_in = 0
        self._prev_bytes_out = 0
        self._prev_pkts_in = 0
        self._prev_pkts_out = 0
        self._baseline_set = False
        self._cached_nic = None
        self._nic_check_countdown = 0
        self.on_updated = None

    def start(self):
        self._stopped = False
        self._baseline_set = False
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()

    def stop(self):
        self._stopped = True

    def _run(self):
        while not self._stopped:
            self._sample()
            deadline = time.time() + 1.0
            while time.time() < deadline and not self._stopped:
                time.sleep(0.05)

    @staticmethod
    def _pick_nic():
        stats = psutil.net_if_stats()
        addresses = psutil.net_if_addrs()

        candidates = []
        for name, s in stats.items():
            if not s.isup:
                continue
            if name == "lo":
                continue
            if any(
                name.startswith(p)
                for p in ("docker", "veth", "br-", "tun", "virbr", "tap", "lb")
            ):
                continue
            candidates.append(name)

        for name in candidates:
            if any(name.startswith(p) for p in ("wl", "wlan", "wifi")):
                return name
        for name in candidates:
            if any(name.startswith(p) for p in ("en", "eth")):
                return name
        return candidates[0] if candidates else None

    def _sample(self):
        self._nic_check_countdown -= 1
        if self._nic_check_countdown <= 0:
            self._nic_check_countdown = 10
            fresh = self._pick_nic()
            if fresh != self._cached_nic:
                self._cached_nic = fresh
                self._prev_bytes_in = 0
                self._prev_bytes_out = 0
                self._prev_pkts_in = 0
                self._prev_pkts_out = 0
                self._baseline_set = False

        if not self._cached_nic:
            return

        try:
            cnt = psutil.net_io_counters(pernic=True).get(self._cached_nic)
            if not cnt:
                return
        except Exception:
            return

        if not self._baseline_set:
            self._prev_bytes_in = cnt.bytes_recv
            self._prev_bytes_out = cnt.bytes_sent
            self._prev_pkts_in = cnt.packets_recv
            self._prev_pkts_out = cnt.packets_sent
            self._baseline_set = True
            if self.on_updated:
                self.on_updated(TrafficStats())
            return

        d_in = max(0, cnt.bytes_recv - self._prev_bytes_in)
        d_out = max(0, cnt.bytes_sent - self._prev_bytes_out)
        dp_in = max(0, cnt.packets_recv - self._prev_pkts_in)
        dp_out = max(0, cnt.packets_sent - self._prev_pkts_out)

        self._prev_bytes_in = cnt.bytes_recv
        self._prev_bytes_out = cnt.bytes_sent
        self._prev_pkts_in = cnt.packets_recv
        self._prev_pkts_out = cnt.packets_sent

        if self.on_updated:
            self.on_updated(TrafficStats(d_in, d_out, dp_in, dp_out))

    def dispose(self):
        self.stop()
