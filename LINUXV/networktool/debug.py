import os
import tempfile
from datetime import datetime

import gi

gi.require_version("Gtk", "3.0")
gi.require_version("Gdk", "3.0")
from gi.repository import Gtk, Gdk, Pango


LOG_FILE = os.path.join(
    tempfile.gettempdir(),
    f"Networktool_{datetime.now():%Y%m%d}.log",
)


class DebugWindow(Gtk.Window):
    _instance = None

    def __init__(self):
        super().__init__(type=Gtk.WindowType.TOPLEVEL)
        DebugWindow._instance = self
        self.set_title("Networktool Debug")
        self.set_default_size(600, 400)
        self.set_position(Gtk.WindowPosition.NONE)

        self._log = Gtk.TextView()
        self._log.set_editable(False)
        self._log.set_cursor_visible(False)
        self._log.set_wrap_mode(Gtk.WrapMode.NONE)
        self._log.modify_font(Pango.FontDescription("monospace 9"))
        self._log.modify_bg(Gtk.StateType.NORMAL, Gdk.color_parse("#0a0a0a"))
        self._log.modify_fg(Gtk.StateType.NORMAL, Gdk.color_parse("#00ff00"))

        self._buf = self._log.get_buffer()

        scroll = Gtk.ScrolledWindow()
        scroll.set_policy(Gtk.PolicyType.AUTOMATIC, Gtk.PolicyType.AUTOMATIC)
        scroll.add(self._log)

        clear_btn = Gtk.Button(label="Clear")
        clear_btn.connect("clicked", lambda b: self._buf.set_text(""))

        vbox = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
        vbox.pack_start(scroll, True, True, 0)
        vbox.pack_start(clear_btn, False, False, 0)
        self.add(vbox)

        self.connect("delete-event", lambda w, e: self.hide() or True)

    @classmethod
    def log(cls, msg, tag=None, fg=None):
        win = cls._instance
        if not win:
            return
        buf = win._buf
        ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        buf.insert(buf.get_end_iter(), f"[{ts}] {msg}\n")

    @classmethod
    def info(cls, msg):
        cls.log(msg, "INFO")

    @classmethod
    def warn(cls, msg):
        cls._write_file("WARN", msg)
        cls.log(msg, "WARN")

    @classmethod
    def error(cls, msg):
        cls._write_file("ERROR", msg)
        cls.log(msg, "ERROR")

    @classmethod
    def data(cls, msg):
        cls.log(msg, "DATA")

    @staticmethod
    def _write_file(level, msg):
        try:
            with open(LOG_FILE, "a") as f:
                ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
                f.write(f"[{ts}] [{level}] {msg}\n")
        except Exception:
            pass

    @staticmethod
    def current_log_path():
        return LOG_FILE
