import json
import os
import re
import shutil


SETTINGS_DIR = os.path.join(os.path.expanduser("~"), ".config", "networktool")
SETTINGS_PATH = os.path.join(SETTINGS_DIR, "settings.json")
BW_DIR = os.path.join(SETTINGS_DIR, "bw")


COLOUR_ONLINE_DEFAULT = 0xFF32C850
COLOUR_OFFLINE_DEFAULT = 0xFFC83232
COLOUR_TITLE_BAR_DEFAULT = 0xFF191919
COLOUR_BACKGROUND_DEFAULT = 0xFF0F0F0F
COLOUR_GRAPH_DL_DEFAULT = 0xFF3C8CDC
COLOUR_GRAPH_UL_DEFAULT = 0xFFDC5096
COLOUR_GRAPH_PING_DEFAULT = 0xFF50C878


class AppSettings:
    def __init__(self):
        self.ping_target = "8.8.8.8"
        self._ping_interval_ms = 2000
        self.fails_before_swap = 3
        self.always_on_top = True
        self.start_with_windows = False
        self.auto_swap = True
        self.hidden_networks = []
        self.hidden_bssids = []
        self.auto_swap_order = []
        self.window_x = 100
        self.window_y = 100
        self.window_width = 260
        self.window_height = 300
        self.opacity = 95
        self.traffic_show_bits = True
        self.show_signal_bars = True
        self.show_speed_graph = True
        self.show_ping_graph = True
        self.colour_online = COLOUR_ONLINE_DEFAULT
        self.colour_offline = COLOUR_OFFLINE_DEFAULT
        self.colour_title_bar = COLOUR_TITLE_BAR_DEFAULT
        self.colour_background = COLOUR_BACKGROUND_DEFAULT
        self.colour_graph_dl = COLOUR_GRAPH_DL_DEFAULT
        self.colour_graph_ul = COLOUR_GRAPH_UL_DEFAULT
        self.colour_graph_ping = COLOUR_GRAPH_PING_DEFAULT

    @property
    def ping_interval_ms(self):
        return self._ping_interval_ms

    @ping_interval_ms.setter
    def ping_interval_ms(self, value):
        self._ping_interval_ms = max(500, value)

    def is_hidden(self, ssid, bssid=""):
        if any(h.lower() == ssid.lower() for h in self.hidden_networks):
            return True
        if bssid and any(h.lower() == bssid.lower() for h in self.hidden_bssids):
            return True
        return False

    @staticmethod
    def load():
        try:
            if os.path.exists(SETTINGS_PATH):
                with open(SETTINGS_PATH) as f:
                    data = json.load(f)
                s = AppSettings()
                for key, value in data.items():
                    if hasattr(s, key):
                        setattr(s, key, value)
                return s
        except Exception:
            pass
        return AppSettings()

    def save(self):
        try:
            os.makedirs(SETTINGS_DIR, exist_ok=True)
            with open(SETTINGS_PATH, "w") as f:
                json.dump(self.__dict__, f, indent=2)
        except Exception:
            pass


class BandwidthRecord:
    def __init__(self, gb_down=0.0, gb_up=0.0):
        self.gb_down = gb_down
        self.gb_up = gb_up

    @property
    def gb_total(self):
        return self.gb_down + self.gb_up


class BandwidthStore:
    def __init__(self):
        self._data = {}
        self._active_ssid = None
        self._load_all()

    def add(self, ssid, bytes_down, bytes_up):
        if ssid not in self._data:
            self._data[ssid] = BandwidthRecord()
        rec = self._data[ssid]
        rec.gb_down += bytes_down / 1e9
        rec.gb_up += bytes_up / 1e9
        self._active_ssid = ssid

    def on_network_changed(self, new_ssid):
        if self._active_ssid and self._active_ssid != new_ssid:
            self._write_one(self._active_ssid)
        self._active_ssid = new_ssid

    def flush(self):
        if self._active_ssid:
            self._write_one(self._active_ssid)

    def try_get(self, ssid):
        return self._data.get(ssid)

    def contains(self, ssid):
        return ssid in self._data

    def clear(self, ssid):
        self._data.pop(ssid, None)
        try:
            os.remove(self._file_path(ssid))
        except Exception:
            pass

    def _load_all(self):
        if not os.path.isdir(BW_DIR):
            return
        for name in os.listdir(BW_DIR):
            if not name.endswith(".json"):
                continue
            try:
                path = os.path.join(BW_DIR, name)
                with open(path) as f:
                    obj = json.load(f)
                ssid = obj.get("ssid", "")
                if not ssid:
                    continue
                rec = BandwidthRecord(obj.get("gb_down", 0), obj.get("gb_up", 0))
                self._data[ssid] = rec
            except Exception:
                pass

    def _write_one(self, ssid):
        rec = self._data.get(ssid)
        if not rec:
            return
        try:
            os.makedirs(BW_DIR, exist_ok=True)
            path = self._file_path(ssid)
            with open(path, "w") as f:
                json.dump({"ssid": ssid, "gb_down": rec.gb_down, "gb_up": rec.gb_up}, f)
        except Exception:
            pass

    @staticmethod
    def _file_path(ssid):
        name = re.sub(r"[^\w\-]", "_", ssid) or f"ssid_{abs(hash(ssid))}"
        return os.path.join(BW_DIR, f"{name}.json")
