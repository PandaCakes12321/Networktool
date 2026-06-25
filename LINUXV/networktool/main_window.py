import math
import os
import subprocess
import threading
import time

import gi

gi.require_version("Gtk", "3.0")
gi.require_version("Gdk", "3.0")
from gi.repository import GLib, Gtk, Gdk, Pango

from networktool.settings import AppSettings, BandwidthStore
from networktool.ping import PingMonitor
from networktool.traffic import TrafficMonitor
from networktool.wifi import WifiManager, WifiNetwork
from networktool.graph import ScrollingGraphWidget, format_bytes
from networktool.sound import play_swap_success
from networktool.debug import DebugWindow


def _parse_argb(argb):
    a = (argb >> 24) & 0xFF
    r = (argb >> 16) & 0xFF
    g = (argb >> 8) & 0xFF
    b = argb & 0xFF
    return (r / 255.0, g / 255.0, b / 255.0)


_ACTION_MARGIN = 1


class NetworkButton(Gtk.EventBox):
    def __init__(self, network, width, bw_store):
        super().__init__()
        self._net = network
        self._bw_store = bw_store
        self._hover = False
        self._width = width
        self._preferred_height = 54 if bw_store and bw_store.contains(network.ssid) else 40
        self.set_size_request(width, self._preferred_height)
        self.set_margin_top(_ACTION_MARGIN)
        self.set_margin_bottom(_ACTION_MARGIN)

        self.add_events(
            Gdk.EventMask.BUTTON_PRESS_MASK
            | Gdk.EventMask.ENTER_NOTIFY_MASK
            | Gdk.EventMask.LEAVE_NOTIFY_MASK
        )
        self.connect("enter-notify-event", lambda w, e: self._set_hover(True))
        self.connect("leave-notify-event", lambda w, e: self._set_hover(False))
        self.connect("button-press-event", self._on_click)
        self.connect("draw", self._on_draw)

        self.on_left_click = None
        self.on_right_click = None

    @property
    def current_network(self):
        return self._net

    def update(self, network, width):
        self._net = network
        self._width = width
        nh = 54 if self._bw_store and self._bw_store.contains(network.ssid) else 40
        if nh != self._preferred_height:
            self._preferred_height = nh
            self.set_size_request(width, nh)
        self.queue_draw()

    def _set_hover(self, hover):
        self._hover = hover
        self.queue_draw()

    def _on_click(self, widget, event):
        if event.button == 1:
            if self.on_left_click:
                self.on_left_click(self._net.ssid)
        elif event.button == 3:
            if self.on_right_click:
                self.on_right_click(self._net, event)
        return True

    def _on_draw(self, widget, cr):
        w = self._width
        h = self._preferred_height

        r = 4
        degrees = math.pi / 180.0

        def rounded_rect(c, x, y, w, h, r):
            c.move_to(x + r, y)
            c.line_to(x + w - r, y)
            c.arc(x + w - r, y + r, r, -90 * degrees, 0)
            c.line_to(x + w, y + h - r)
            c.arc(x + w - r, y + h - r, r, 0, 90 * degrees)
            c.line_to(x + r, y + h)
            c.arc(x + r, y + h - r, r, 90 * degrees, 180 * degrees)
            c.line_to(x, y + r)
            c.arc(x + r, y + r, r, 180 * degrees, 270 * degrees)
            c.close_path()

        if self._net.is_connected:
            bg = (0, 0.275, 0.137) if self._hover else (0, 0.2, 0.1)
            border = (0, 0.3, 0.15)
        else:
            bg = (0.176, 0.176, 0.176) if self._hover else (0.118, 0.118, 0.118)
            border = (0.216, 0.216, 0.216)

        cr.set_source_rgb(*bg)
        rounded_rect(cr, 0, 0, w - 1, h - 1, r)
        cr.fill()

        cr.set_source_rgb(*border)
        cr.set_line_width(1)
        rounded_rect(cr, 0, 0, w - 1, h - 1, r)
        cr.stroke()

        bars_right = 27
        bars_x = w - bars_right - 5 if hasattr(NetworkButton, "_show_signal_bars") and NetworkButton._show_signal_bars else w - 10

        if hasattr(NetworkButton, "_show_signal_bars") and NetworkButton._show_signal_bars:
            bars = 4 if self._net.signal_percent >= 75 else 3 if self._net.signal_percent >= 50 else 2 if self._net.signal_percent >= 25 else 1 if self._net.signal_percent > 0 else 0
            for i in range(4):
                bh = 4 + i * 3
                bx = bars_x + i * 5
                by = 12 - bh if i >= 2 else 12 - bh + 20
                by = 6 + (12 - bh)
                col = (0.314, 0.784, 0.314) if i < bars else (0.196, 0.196, 0.196)
                cr.set_source_rgb(*col)
                cr.rectangle(bx, by, 3, bh)
                cr.fill()

        dot_size = 6
        dot_left = 6
        dot_y = (h - dot_size) / 2
        dot_color = (0.235, 0.784, 0.314) if self._net.has_internet else (0.784, 0.235, 0.235)
        cr.set_source_rgb(*dot_color)
        cr.arc(dot_left + dot_size / 2, dot_y + dot_size / 2, dot_size / 2, 0, 2 * math.pi)
        cr.fill()

        text_left = dot_left + dot_size + 6
        text_right = bars_x - 4
        text_color = (0.314, 0.863, 0.471) if self._net.is_connected else (0.784, 0.784, 0.784)
        cr.set_source_rgb(*text_color)
        cr.select_font_face("Sans", 0, 1 if self._net.is_connected else 0)
        cr.set_font_size(11)

        max_text_w = text_right - text_left
        ssid = self._net.ssid
        ext = cr.text_extents(ssid)
        text_w = min(ext.width, max_text_w)

        cr.move_to(text_left, 16 if (self._bw_store and self._bw_store.contains(self._net.ssid)) else (h + 9) / 2 + 9)
        if ext.width > max_text_w:
            while cr.text_extents(ssid + "...").width > max_text_w and len(ssid) > 1:
                ssid = ssid[:-1]
            ssid += "..."
        cr.show_text(ssid)

        if self._net.is_saved and not self._net.is_connected:
            tag_x = text_left + text_w + 4
            if tag_x + 36 < bars_x - 2:
                cr.set_source_rgb(0, 0.314, 0.157)
                cr.rectangle(tag_x, 4, 36, 13)
                cr.fill()
                cr.set_source_rgb(0.235, 0.706, 0.392)
                cr.select_font_face("Sans", 0, 0)
                cr.set_font_size(8)
                cr.move_to(tag_x + 4, 14)
                cr.show_text("saved")

        bw = self._bw_store.try_get(self._net.ssid) if self._bw_store else None
        if bw:
            bw_text = f"\u2193 {bw.gb_down:.3f} GB   \u2191 {bw.gb_up:.3f} GB   Total: {bw.gb_total:.3f} GB"
            cr.set_source_rgb(0.627, 0.627, 0.627)
            cr.select_font_face("Sans", 0, 0)
            cr.set_font_size(8)
            cr.move_to(text_left, h - 4)
            cr.show_text(bw_text)
        elif self._net.bssid:
            cr.set_source_rgb(0.275, 0.275, 0.275)
            cr.select_font_face("monospace", 0, 0)
            cr.set_font_size(7)
            cr.move_to(text_left, h - 4)
            cr.show_text(self._net.bssid)


NetworkButton._show_signal_bars = True


class SettingsDialog(Gtk.Dialog):
    def __init__(self, parent, settings, all_networks, on_opacity_changed=None):
        super().__init__(
            title="Networktool Settings",
            transient_for=parent,
            flags=Gtk.DialogFlags.MODAL,
        )
        self._settings = settings
        self._all_networks = all_networks
        self._on_opacity_changed = on_opacity_changed

        self.set_default_size(440, 545)
        self.set_position(Gtk.WindowPosition.CENTER_ON_PARENT)

        css = b"""
        .settings-window { background-color: #141414; color: #ccc; }
        .settings-window label { color: #ccc; }
        .settings-tab { background-color: #141414; }
        .settings-tab button { background-color: #232323; color: #aaa; border: none; }
        .settings-tab button:hover { background-color: #2a2a2a; }
        .settings-tab button:checked { background-color: #141414; color: white; }
        """
        provider = Gtk.CssProvider()
        provider.load_from_data(css)
        self.get_style_context().add_provider(provider, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)

        self._colours = {}

        self._build_ui()

    def _build_ui(self):
        vbox = self.get_content_area()
        vbox.set_spacing(0)

        notebook = Gtk.Notebook()
        notebook.set_show_border(False)
        notebook.set_show_tabs(False)
        notebook.set_scrollable(False)

        pages = [
            ("General", self._build_general_page()),
            ("Colours", self._build_colours_page()),
            ("Hidden Networks", self._build_hidden_page()),
            ("Swap Priority", self._build_priority_page()),
        ]

        tab_bar = Gtk.Box(spacing=0)
        for i, (title, page) in enumerate(pages):
            btn = Gtk.Button(label=title)
            btn.set_relief(Gtk.ReliefStyle.NONE)
            btn.set_size_request(440 // len(pages), 32)
            ctx = btn.get_style_context()
            ctx.add_class("settings-tab")

            if i == 0:
                btn.set_name("active-tab")

            def _switch(idx, b):
                def cb(*_):
                    notebook.set_current_page(idx)
                    for c in tab_bar.get_children():
                        c.set_name("")
                    b.set_name("active-tab")

                return cb

            btn.connect("clicked", _switch(i, btn))
            tab_bar.pack_start(btn, True, True, 0)

        vbox.pack_start(tab_bar, False, False, 0)
        for title, page in pages:
            page_ctx = page.get_style_context()
            page_ctx.add_class("settings-tab")
            notebook.append_page(page, Gtk.Label(label=title))

        vbox.pack_start(notebook, True, True, 0)

        bb = Gtk.Box(spacing=8)
        bb.set_margin_top(8)
        bb.set_margin_bottom(8)
        bb.set_margin_start(12)
        bb.set_margin_end(12)

        save_btn = Gtk.Button(label="Save")
        save_btn.connect("clicked", lambda b: self._save())
        cancel_btn = Gtk.Button(label="Cancel")
        cancel_btn.connect("clicked", lambda b: self.response(Gtk.ResponseType.CANCEL))

        bb.pack_end(save_btn, False, False, 0)
        bb.pack_end(cancel_btn, False, False, 0)
        vbox.pack_end(bb, False, False, 0)

        self._notebook = notebook
        self._tab_btns = [c for c in tab_bar.get_children()]

    def _make_label(self, text, x, y):
        lbl = Gtk.Label(label=text)
        lbl.set_halign(Gtk.Align.START)
        lbl.set_margin_top(y)
        lbl.set_margin_start(x)
        return lbl

    def _build_general_page(self):
        page = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
        page.set_margin_start(12)
        page.set_margin_top(12)

        y = 0
        page.pack_start(self._make_label("Ping target (IP or hostname):", 0, y), False, False, 0)
        y += 22
        self._ping_target_entry = Gtk.Entry()
        self._ping_target_entry.set_text(self._settings.ping_target)
        self._ping_target_entry.set_size_request(220, -1)
        self._ping_target_entry.set_margin_top(y)
        page.pack_start(self._ping_target_entry, False, False, 0)
        y += 38

        page.pack_start(self._make_label("Consecutive fails before auto-swap:", 0, y), False, False, 0)
        y += 22
        self._fails_spinner = Gtk.SpinButton.new_with_range(1, 20, 1)
        self._fails_spinner.set_value(self._settings.fails_before_swap)
        self._fails_spinner.set_margin_top(y)
        self._fails_spinner.set_size_request(70, -1)
        page.pack_start(self._fails_spinner, False, False, 0)
        y += 38

        page.pack_start(self._make_label("Ping interval (ms):", 0, y), False, False, 0)
        y += 22
        self._ping_interval_spinner = Gtk.SpinButton.new_with_range(500, 30000, 500)
        self._ping_interval_spinner.set_value(self._settings.ping_interval_ms)
        self._ping_interval_spinner.set_margin_top(y)
        self._ping_interval_spinner.set_size_request(90, -1)
        page.pack_start(self._ping_interval_spinner, False, False, 0)
        y += 44

        self._always_on_top_cb = Gtk.CheckButton(label="Always on top")
        self._always_on_top_cb.set_active(self._settings.always_on_top)
        self._always_on_top_cb.set_margin_top(y)
        page.pack_start(self._always_on_top_cb, False, False, 0)
        y += 30

        self._startup_cb = Gtk.CheckButton(label="Start with session")
        self._startup_cb.set_active(self._settings.start_with_windows)
        self._startup_cb.set_margin_top(y)
        page.pack_start(self._startup_cb, False, False, 0)
        y += 30

        self._auto_swap_cb = Gtk.CheckButton(label="Auto-swap network when offline")
        self._auto_swap_cb.set_active(self._settings.auto_swap)
        self._auto_swap_cb.set_margin_top(y)
        page.pack_start(self._auto_swap_cb, False, False, 0)
        y += 30

        self._traffic_bits_cb = Gtk.CheckButton(label="Speed in bits/s (Mbps) - uncheck for bytes/s (MB/s)")
        self._traffic_bits_cb.set_active(self._settings.traffic_show_bits)
        self._traffic_bits_cb.set_margin_top(y)
        page.pack_start(self._traffic_bits_cb, False, False, 0)
        y += 30

        self._signal_bars_cb = Gtk.CheckButton(label="Show signal strength bars")
        self._signal_bars_cb.set_active(self._settings.show_signal_bars)
        self._signal_bars_cb.set_margin_top(y)
        page.pack_start(self._signal_bars_cb, False, False, 0)
        y += 30

        self._speed_graph_cb = Gtk.CheckButton(label="Show speed graph (download / upload)")
        self._speed_graph_cb.set_active(self._settings.show_speed_graph)
        self._speed_graph_cb.set_margin_top(y)
        page.pack_start(self._speed_graph_cb, False, False, 0)
        y += 30

        self._ping_graph_cb = Gtk.CheckButton(label="Show ping graph")
        self._ping_graph_cb.set_active(self._settings.show_ping_graph)
        self._ping_graph_cb.set_margin_top(y)
        page.pack_start(self._ping_graph_cb, False, False, 0)
        y += 38

        page.pack_start(self._make_label(f"Transparency: {self._settings.opacity}%", 0, y), False, False, 0)
        y += 22
        self._opacity_scale = Gtk.Scale.new_with_range(
            Gtk.Orientation.HORIZONTAL, 20, 100, 5
        )
        self._opacity_scale.set_value(self._settings.opacity)
        self._opacity_scale.set_size_request(340, -1)
        self._opacity_scale.set_margin_top(y)
        self._opacity_scale.connect("value-changed", self._on_opacity_changed_cb)
        page.pack_start(self._opacity_scale, False, False, 0)

        self._opacity_label = page.get_children()[-3]

        return page

    def _on_opacity_changed_cb(self, scale):
        val = int(scale.get_value())
        self._opacity_label.set_text(f"Transparency: {val}%")
        if self._on_opacity_changed:
            self._on_opacity_changed(val)

    def _build_colours_page(self):
        page = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
        page.set_margin_start(12)
        page.set_margin_top(12)

        def argb_to_gdk(argb):
            r = (argb >> 16) & 0xFF
            g = (argb >> 8) & 0xFF
            b = argb & 0xFF
            return Gdk.RGBA(r / 255.0, g / 255.0, b / 255.0, 1.0)

        colour_keys = [
            ("Online colour", "colour_online", self._settings.colour_online),
            ("Offline colour", "colour_offline", self._settings.colour_offline),
            ("Title bar colour", "colour_title_bar", self._settings.colour_title_bar),
            ("Background colour", "colour_background", self._settings.colour_background),
            ("Graph - download", "colour_graph_dl", self._settings.colour_graph_dl),
            ("Graph - upload", "colour_graph_ul", self._settings.colour_graph_ul),
            ("Graph - ping", "colour_graph_ping", self._settings.colour_graph_ping),
        ]

        y = 0
        for label, key, default_argb in colour_keys:
            hbox = Gtk.Box(spacing=12)
            hbox.set_margin_top(y)
            lbl = Gtk.Label(label=label)
            lbl.set_halign(Gtk.Align.START)

            rgba = argb_to_gdk(default_argb)
            color_btn = Gtk.ColorButton.new_with_rgba(rgba)
            color_btn.set_size_request(50, 24)
            color_btn.set_title(label)
            self._colours[key] = default_argb
            color_btn.connect("color-set", lambda b, k=key: self._colours.__setitem__(k, b.get_rgba().to_string()))

            hbox.pack_start(lbl, False, False, 0)
            hbox.pack_end(color_btn, False, False, 0)
            page.pack_start(hbox, False, False, 0)
            y += 32

        hint = Gtk.Label(label="Click a swatch to change colour.")
        hint.set_margin_top(y)
        page.pack_start(hint, False, False, 0)

        return page

    def _build_hidden_page(self):
        page = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
        page.set_margin_start(12)
        page.set_margin_top(12)

        hint = Gtk.Label(label="Checked = hidden. Uncheck to unhide.")
        hint.set_halign(Gtk.Align.START)
        page.pack_start(hint, False, False, 0)

        scroll = Gtk.ScrolledWindow()
        scroll.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        scroll.set_size_request(390, 340)
        scroll.set_margin_top(12)

        self._hidden_store = Gtk.ListStore(bool, str, str)
        for net in self._all_networks:
            is_hidden = self._settings.is_hidden(net, "")
            self._hidden_store.append([is_hidden, net, "ssid"])

        for bssid in self._settings.hidden_bssids:
            self._hidden_store.append([True, f"BSSID: {bssid}", "bssid"])

        for ssid in self._settings.hidden_networks:
            if ssid not in [r[1] for r in self._hidden_store if r[2] == "ssid"]:
                self._hidden_store.append([True, ssid, "ssid"])

        treeview = Gtk.TreeView(model=self._hidden_store)
        treeview.set_headers_visible(False)

        renderer_toggle = Gtk.CellRendererToggle()
        renderer_toggle.connect("toggled", self._on_hidden_toggle)
        col_toggle = Gtk.TreeViewColumn("Hidden", renderer_toggle, active=0)
        treeview.append_column(col_toggle)

        renderer_text = Gtk.CellRendererText()
        col_text = Gtk.TreeViewColumn("Network", renderer_text, text=1)
        treeview.append_column(col_text)

        scroll.add(treeview)
        page.pack_start(scroll, True, True, 0)
        self._hidden_treeview = treeview

        return page

    def _on_hidden_toggle(self, renderer, path):
        self._hidden_store[path][0] = not self._hidden_store[path][0]

    def _build_priority_page(self):
        page = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
        page.set_margin_start(12)
        page.set_margin_top(12)

        hint = Gtk.Label(label="Auto-swap tries top entries first:")
        hint.set_halign(Gtk.Align.START)
        page.pack_start(hint, False, False, 0)

        hbox = Gtk.Box(spacing=8)
        hbox.set_margin_top(12)

        self._priority_store = Gtk.ListStore(str)
        visible = [n for n in self._all_networks if not self._settings.is_hidden(n, "")]
        ordered = [n for n in self._settings.auto_swap_order if n in visible]
        rest = [n for n in visible if n not in ordered]
        for n in ordered + rest:
            self._priority_store.append([n])

        treeview = Gtk.TreeView(model=self._priority_store)
        treeview.set_headers_visible(False)
        renderer_text = Gtk.CellRendererText()
        treeview.append_column(Gtk.TreeViewColumn("Network", renderer_text, text=0))
        treeview.set_size_request(290, 320)

        btn_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
        up_btn = Gtk.Button(label="\u25b2 Up")
        dn_btn = Gtk.Button(label="\u25bc Down")
        up_btn.connect("clicked", lambda b: self._move_priority(treeview, -1))
        dn_btn.connect("clicked", lambda b: self._move_priority(treeview, 1))
        up_btn.set_size_request(90, 30)
        dn_btn.set_size_request(90, 30)
        btn_box.pack_start(up_btn, False, False, 0)
        btn_box.pack_start(dn_btn, False, False, 0)

        hbox.pack_start(treeview, False, False, 0)
        hbox.pack_start(btn_box, False, False, 0)
        page.pack_start(hbox, False, False, 0)
        self._priority_treeview = treeview

        return page

    def _move_priority(self, treeview, direction):
        sel = treeview.get_selection()
        model, iter_ = sel.get_selected()
        if not iter_:
            return
        row = model.get_path(iter_).get_indices()[0]
        new_row = row + direction
        if new_row < 0 or new_row >= len(model):
            return
        val = model[row][0]
        model.remove(iter_)
        model.insert(new_row, [val])
        sel.select_path(new_row)

    def _save(self):
        self._settings.ping_target = self._ping_target_entry.get_text().strip()
        self._settings.fails_before_swap = int(self._fails_spinner.get_value())
        self._settings.ping_interval_ms = int(self._ping_interval_spinner.get_value())
        self._settings.always_on_top = self._always_on_top_cb.get_active()
        self._settings.start_with_windows = self._startup_cb.get_active()
        self._settings.auto_swap = self._auto_swap_cb.get_active()
        self._settings.traffic_show_bits = self._traffic_bits_cb.get_active()
        self._settings.show_signal_bars = self._signal_bars_cb.get_active()
        self._settings.show_speed_graph = self._speed_graph_cb.get_active()
        self._settings.show_ping_graph = self._ping_graph_cb.get_active()
        self._settings.opacity = int(self._opacity_scale.get_value())

        for key, rgba_str in self._colours.items():
            rgba = Gdk.RGBA()
            rgba.parse(rgba_str)
            to_argb = lambda r, g, b: (0xFF << 24) | (int(r * 255) << 16) | (int(g * 255) << 8) | int(b * 255)
            setattr(self._settings, key, to_argb(rgba.red, rgba.green, rgba.blue))

        self._settings.hidden_networks.clear()
        self._settings.hidden_bssids.clear()
        for row in self._hidden_store:
            if not row[0]:
                continue
            val = row[1]
            if val.startswith("BSSID: "):
                self._settings.hidden_bssids.append(val[7:])
            else:
                self._settings.hidden_networks.append(val)

        self._settings.auto_swap_order = [r[0] for r in self._priority_store]

        self._set_startup(self._settings.start_with_windows)
        self._settings.save()
        self.response(Gtk.ResponseType.OK)

    @staticmethod
    def _set_startup(enable):
        autostart_dir = os.path.join(os.path.expanduser("~"), ".config", "autostart")
        desktop_file = os.path.join(autostart_dir, "networktool.desktop")
        if enable:
            try:
                os.makedirs(autostart_dir, exist_ok=True)
                exe = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "main.py"))
                content = f"""[Desktop Entry]
Type=Application
Name=Networktool
Exec=python3 {exe}
Terminal=false
X-GNOME-Autostart-enabled=true
"""
                with open(desktop_file, "w") as f:
                    f.write(content)
            except Exception:
                pass
        else:
            try:
                if os.path.exists(desktop_file):
                    os.remove(desktop_file)
            except Exception:
                pass


class MainWindow(Gtk.Window):
    def __init__(self):
        super().__init__(type=Gtk.WindowType.TOPLEVEL)
        self._settings = AppSettings.load()
        self._exiting = False
        self._is_swapping = False
        self._is_scanning = False
        self._last_connected_ssid = None
        self._last_known_ping_ms = 0
        self._networks = []
        self._dot_color = (0.314, 0.784, 0.314)
        self._drag_data = {"dragging": False, "start": (0, 0)}
        self._resize_data = {"resizing": False, "start": (0, 0), "start_size": (0, 0)}

        self.ping_monitor = PingMonitor(self._settings)
        self.traffic_monitor = TrafficMonitor()
        self.bw_store = BandwidthStore()

        NetworkButton._bw_store = self.bw_store
        self.set_decorated(False)
        self.set_keep_above(self._settings.always_on_top)
        self.set_skip_taskbar_hint(True)
        self.stick()

        screen = Gdk.Screen.get_default()
        css_provider = Gtk.CssProvider()
        css_provider.load_from_data(
            b"""
        .dark-widget { background-color: #0f0f0f; color: #ccc; }
        .dark-row { background-color: #141414; }
        .title-bar-btn { 
            background: transparent; border: none; color: #777; 
            padding: 0; min-width: 26px; min-height: 28px; 
        }
        .title-bar-btn:hover { 
            background: rgba(255,255,255,0.05); 
        }
        .title-bar-btn:active { 
            background: rgba(255,255,255,0.1); 
        }
        .close-btn:hover { 
            background: rgba(180,40,40,0.8); color: white; 
        }
        .dark-menu { background-color: #1c1c1c; color: white; }
        .dark-menu .menuitem { background-color: #1c1c1c; color: white; padding: 4px 12px; }
        .dark-menu .menuitem:hover { background-color: #333; }
        .dark-menu .separator { background-color: #333; }
        .network-scroll scrollbar.vertical { 
            min-width: 0; 
            min-height: 0; 
            background: transparent;
        }
        .network-scroll scrollbar.vertical slider { 
            min-width: 0; 
            min-height: 0; 
            background: rgba(255,255,255,0.05); 
        }
        entry, spinbutton { 
            background-color: #232323; color: white; 
            border: 1px solid #3a3a3a; 
        }
        combobox button { 
            background-color: #232323; color: white; 
            border: 1px solid #3a3a3a; 
        }
        checkbutton { color: #ccc; }
        button {
            background-color: #373737; color: #ccc;
            border: none; padding: 4px 8px;
        }
        button:hover { background-color: #444; }
        scale trough { background-color: #2a2a2a; min-height: 4px; }
        scale highlight { background-color: #555; }
        scale slider { background-color: #777; }
        .tray-menu { background-color: #1c1c1c; }
        .tray-menu .menuitem { 
            background-color: #1c1c1c; color: white; 
            padding: 4px 16px; 
        }
        .tray-menu .menuitem:hover { background-color: #333; }
        treeview { 
            background-color: #1e1e1e; color: white; 
        }
        treeview:selected {
            background-color: #2a5a3a;
        }
        """
        )
        Gtk.StyleContext.add_provider_for_screen(
            screen, css_provider, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION
        )

        self.connect("destroy", self._on_destroy)
        self.connect("delete-event", lambda w, e: self.hide() or True)
        self.connect("key-press-event", self._on_key)

        self._build_ui()
        self._setup_tray()
        self._apply_settings()

        self.ping_monitor.on_status_changed = self._on_ping_status
        self.traffic_monitor.on_updated = self._on_traffic_updated

        self.ping_monitor.start()
        self.traffic_monitor.start()

        GLib.timeout_add(5000, self._refresh_networks)

        self.show_all()
        self.present()
        NetworkButton._show_signal_bars = self._settings.show_signal_bars

    def _build_ui(self):
        self.set_default_size(self._settings.window_width, self._settings.window_height)
        self.move(self._settings.window_x, self._settings.window_y)

        self._vbox = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
        self.add(self._vbox)

        self._build_title_bar()
        self._build_status_row()
        self._build_traffic_row()
        self._build_separator()

        self._traffic_graph = ScrollingGraphWidget(
            dual=True,
            scale_levels=[
                10_000, 50_000, 100_000, 250_000, 500_000,
                1_000_000, 2_500_000, 5_000_000, 10_000_000,
                25_000_000, 50_000_000, 100_000_000, 500_000_000, 1_000_000_000,
            ],
            formatter=format_bytes,
        )
        self._traffic_graph.set_size_request(-1, 50)
        self._traffic_graph.set_colours(0.235, 0.549, 0.863, 0.863, 0.314, 0.588)
        self._vbox.pack_start(self._traffic_graph, False, False, 0)

        self._ping_graph = ScrollingGraphWidget(
            dual=False,
            scale_levels=[25, 50, 100, 200, 500, 1000, 2000],
            formatter=lambda v: f"{v}ms",
        )
        self._ping_graph.set_size_request(-1, 32)
        self._ping_graph.set_colours(0.314, 0.784, 0.471)
        self._vbox.pack_start(self._ping_graph, False, False, 0)

        self._build_network_area()
        self._build_resize_grip()

    def _build_title_bar(self):
        self._title_bar = Gtk.EventBox()
        self._title_bar.set_size_request(-1, 28)
        self._title_bar.get_style_context().add_class("dark-widget")
        self._title_bar.connect("button-press-event", self._drag_start)
        self._title_bar.connect("motion-notify-event", self._drag_move)
        self._title_bar.connect("button-release-event", self._drag_end)
        self._title_bar.add_events(
            Gdk.EventMask.BUTTON_PRESS_MASK
            | Gdk.EventMask.BUTTON_RELEASE_MASK
            | Gdk.EventMask.POINTER_MOTION_MASK
        )

        hbox = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=0)

        title_lbl = Gtk.Label(label="NETWORKTOOL")
        title_lbl.set_halign(Gtk.Align.START)
        title_lbl.set_margin_start(6)
        title_lbl.modify_fg(Gtk.StateType.NORMAL, Gdk.color_parse("#b4b4b4"))
        title_lbl.modify_font(Pango.FontDescription("bold 8"))

        self._swap_btn = Gtk.Button(label="\u21c4")
        self._swap_btn.get_style_context().add_class("title-bar-btn")
        self._swap_btn.set_size_request(30, 28)
        self._swap_btn.connect("clicked", self._on_swap_toggle)

        settings_btn = Gtk.Button(label="\u2699")
        settings_btn.get_style_context().add_class("title-bar-btn")
        settings_btn.set_size_request(26, 28)
        settings_btn.modify_font(Pango.FontDescription("normal 11"))
        settings_btn.connect("clicked", self._open_settings)

        close_btn = Gtk.Button(label="\u2715")
        close_btn.get_style_context().add_class("title-bar-btn")
        close_btn.get_style_context().add_class("close-btn")
        close_btn.set_size_request(26, 28)
        close_btn.connect("clicked", lambda b: self.hide())

        self._update_swap_btn()

        hbox.pack_start(title_lbl, True, True, 0)
        hbox.pack_end(close_btn, False, False, 0)
        hbox.pack_end(settings_btn, False, False, 0)
        hbox.pack_end(self._swap_btn, False, False, 0)

        self._title_bar.add(hbox)
        self._vbox.pack_start(self._title_bar, False, False, 0)

    def _build_status_row(self):
        self._status_row = Gtk.EventBox()
        self._status_row.set_size_request(-1, 32)

        self._status_dot = Gtk.DrawingArea()
        self._status_dot.set_size_request(14, 14)
        self._status_dot.set_halign(Gtk.Align.START)
        self._status_dot.set_valign(Gtk.Align.CENTER)
        self._status_dot.set_margin_start(8)
        self._status_dot.connect("draw", self._draw_status_dot)

        self._status_label = Gtk.Label(label="CHECKING...")
        self._status_label.set_margin_start(6)

        self._fail_label = Gtk.Label(label="")
        self._fail_label.set_margin_start(8)

        self._ping_label = Gtk.Label(label="-- ms")
        self._ping_label.set_halign(Gtk.Align.END)

        self._ping_target_label = Gtk.Label(label=f"ping {self._settings.ping_target}")
        self._ping_target_label.set_halign(Gtk.Align.CENTER)

        hbox = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=0)
        hbox.pack_start(self._status_dot, False, False, 0)
        hbox.pack_start(self._status_label, False, False, 0)
        hbox.pack_start(self._fail_label, False, False, 0)
        hbox.pack_start(self._ping_target_label, True, True, 0)
        hbox.pack_end(self._ping_label, False, False, 0)

        self._status_row.add(hbox)
        self._vbox.pack_start(self._status_row, False, False, 0)

    def _draw_status_dot(self, widget, cr):
        alloc = widget.get_allocation()
        w = alloc.width
        h = alloc.height
        r = min(w, h) / 2 - 1
        cx = w / 2
        cy = h / 2
        cr.set_source_rgb(*self._dot_color)
        cr.arc(cx, cy, r, 0, 2 * math.pi)
        cr.fill()

    def _build_traffic_row(self):
        row = Gtk.EventBox()
        row.set_size_request(-1, 22)

        self._down_lbl = Gtk.Label(label="\u2193 0.00 Mb/s")
        self._up_lbl = Gtk.Label(label="\u2191 0.00 Mb/s")
        self._pkt_lbl = Gtk.Label(label="0 / 0 pkt/s")

        self._down_lbl.set_margin_start(8)
        self._down_lbl.set_size_request(88, -1)

        self._up_lbl.set_size_request(88, -1)

        hbox = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=0)
        hbox.pack_start(self._down_lbl, False, False, 0)
        hbox.pack_start(self._up_lbl, False, False, 0)
        hbox.pack_end(self._pkt_lbl, False, False, 0)

        row.add(hbox)
        self._vbox.pack_start(row, False, False, 0)

    def _build_separator(self):
        sep = Gtk.EventBox()
        sep.set_size_request(-1, 1)
        sep.modify_bg(Gtk.StateType.NORMAL, Gdk.color_parse("#282828"))
        self._vbox.pack_start(sep, False, False, 0)

    def _build_network_area(self):
        header = Gtk.EventBox()
        header.set_size_request(-1, 20)
        header.modify_bg(Gtk.StateType.NORMAL, Gdk.color_parse("#191919"))
        header_lbl = Gtk.Label(label="AVAILABLE NETWORKS")
        header_lbl.set_margin_start(8)
        header_lbl.modify_fg(Gtk.StateType.NORMAL, Gdk.color_parse("#505050"))
        header_lbl.modify_font(Pango.FontDescription("bold 7"))
        header_lbl.set_halign(Gtk.Align.START)
        header.add(header_lbl)
        self._vbox.pack_start(header, False, False, 0)

        self._network_scroll = Gtk.ScrolledWindow()
        self._network_scroll.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        self._network_scroll.get_style_context().add_class("network-scroll")

        self._network_vbox = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0)
        self._network_vbox.set_margin_start(4)
        self._network_vbox.set_margin_end(4)

        self._network_viewport = Gtk.Viewport()
        self._network_viewport.set_shadow_type(Gtk.ShadowType.NONE)
        self._network_viewport.add(self._network_vbox)
        self._network_scroll.add(self._network_viewport)
        self._network_scroll.connect("size-allocate", self._on_network_scroll_resize)
        self._vbox.pack_start(self._network_scroll, True, True, 0)

    def _build_resize_grip(self):
        self._grip = Gtk.EventBox()
        self._grip.set_size_request(-1, 8)
        self._grip.modify_bg(Gtk.StateType.NORMAL, Gdk.color_parse("#161616"))
        self._grip.connect("realize", lambda w: w.get_window().set_cursor(Gdk.Cursor.new(Gdk.CursorType.BOTTOM_RIGHT_CORNER)))
        self._grip.connect("draw", self._draw_grip)
        self._grip.connect("button-press-event", self._resize_start)
        self._grip.connect("motion-notify-event", self._resize_move)
        self._grip.connect("button-release-event", self._resize_end)
        self._grip.add_events(
            Gdk.EventMask.BUTTON_PRESS_MASK
            | Gdk.EventMask.BUTTON_RELEASE_MASK
            | Gdk.EventMask.POINTER_MOTION_MASK
        )
        self._vbox.pack_start(self._grip, False, False, 0)

    def _draw_grip(self, widget, cr):
        alloc = widget.get_allocation()
        w = alloc.width
        h = alloc.height
        cr.set_source_rgb(0.235, 0.235, 0.235)
        cr.set_line_width(1)
        cr.move_to(w - 8, h - 2)
        cr.line_to(w - 2, h - 8)
        cr.stroke()
        cr.move_to(w - 5, h - 2)
        cr.line_to(w - 2, h - 5)
        cr.stroke()

    def _drag_start(self, widget, event):
        if event.button == 1:
            self._drag_data["dragging"] = True
            self._drag_data["start"] = (event.x_root, event.y_root)
            self._drag_data["win_start"] = self.get_position()
        return True

    def _drag_move(self, widget, event):
        if self._drag_data["dragging"]:
            dx = event.x_root - self._drag_data["start"][0]
            dy = event.y_root - self._drag_data["start"][1]
            wx = self._drag_data["win_start"][0] + dx
            wy = self._drag_data["win_start"][1] + dy
            self.move(int(wx), int(wy))
        return True

    def _drag_end(self, widget, event):
        self._drag_data["dragging"] = False
        self._save_window_bounds()
        return True

    def _resize_start(self, widget, event):
        if event.button == 1:
            self._resize_data["resizing"] = True
            self._resize_data["start"] = (event.x_root, event.y_root)
            self._resize_data["start_size"] = self.get_size()
        return True

    def _resize_move(self, widget, event):
        if self._resize_data["resizing"]:
            dx = event.x_root - self._resize_data["start"][0]
            dy = event.y_root - self._resize_data["start"][1]
            sw = max(180, self._resize_data["start_size"][0] + dx)
            sh = max(150, self._resize_data["start_size"][1] + dy)
            self.resize(int(sw), int(sh))
        return True

    def _resize_end(self, widget, event):
        self._resize_data["resizing"] = False
        self._save_window_bounds()
        return True

    def _on_key(self, widget, event):
        if event.keyval == Gdk.KEY_Escape:
            self.hide()
            return True
        return False

    def _on_network_scroll_resize(self, widget, alloc):
        width = alloc.width - 12
        width = max(80, width)
        for c in self._network_vbox.get_children():
            if isinstance(c, NetworkButton):
                c.update(c.current_network, width)

    def _setup_tray(self):
        self._tray_menu = Gtk.Menu()
        self._tray_menu.get_style_context().add_class("tray-menu")

        def add_tray_item(text, cb):
            item = Gtk.MenuItem(label=text)
            item.connect("activate", lambda w: cb())
            self._tray_menu.append(item)

        add_tray_item("Show", lambda: self._show_window())
        add_tray_item("Settings", lambda: self._open_settings(None))
        add_tray_item("Open Log File", lambda: self._open_log())
        add_tray_item("Debug Window", lambda: self._toggle_debug())
        self._tray_menu.append(Gtk.SeparatorMenuItem())
        add_tray_item("Exit", lambda: self._exit_app())

        self._tray_icon = None
        try:
            icon_path = os.path.join(os.path.dirname(__file__), "..", "icon.png")
            if not os.path.exists(icon_path):
                icon_path = None

            self._tray_icon = Gtk.StatusIcon()
            if icon_path:
                self._tray_icon.set_from_file(icon_path)
            else:
                theme = Gtk.IconTheme.get_default()
                try:
                    pix = theme.load_icon("network-workgroup", 22, 0)
                    self._tray_icon.set_from_pixbuf(pix)
                except Exception:
                    self._tray_icon.set_from_stock(Gtk.STOCK_NETWORK)
            self._tray_icon.set_tooltip_text("Networktool")

            self._tray_icon.connect("activate", lambda icon: self._show_window())

            def on_tray_popup(icon, button, time):
                self._tray_menu.show_all()
                self._tray_menu.popup(None, None, None, None, button, time)

            self._tray_icon.connect("popup-menu", on_tray_popup)
        except Exception:
            pass

    def _show_window(self):
        self.show_all()
        self.present()
        self.deiconify()
        self.get_window().focus(Gdk.CURRENT_TIME)

    def _open_log(self):
        log_path = DebugWindow.current_log_path()
        try:
            subprocess.Popen(["xdg-open", log_path])
        except Exception:
            pass

    def _toggle_debug(self):
        dw = DebugWindow._instance
        if dw and dw.get_visible():
            dw.hide()
        else:
            DebugWindow()
            dw = DebugWindow._instance
            if dw:
                dw.show_all()

    def _exit_app(self):
        self._exiting = True
        self._save_window_bounds()
        self.ping_monitor.dispose()
        self.traffic_monitor.dispose()
        self.bw_store.flush()
        self._settings.save()
        Gtk.main_quit()

    def _on_destroy(self, widget):
        if not self._exiting:
            self._exit_app()

    def _save_window_bounds(self):
        x, y = self.get_position()
        w, h = self.get_size()
        self._settings.window_x = x
        self._settings.window_y = y
        self._settings.window_width = w
        self._settings.window_height = h
        self._settings.save()

    def _apply_settings(self):
        self.set_keep_above(self._settings.always_on_top)
        op = max(0.2, min(1.0, self._settings.opacity / 100.0))
        self.set_opacity(op)

        r, g, b = _parse_argb(self._settings.colour_title_bar)
        self._title_bar.modify_bg(Gtk.StateType.NORMAL, Gdk.color_parse(f"#{int(r*255):02x}{int(g*255):02x}{int(b*255):02x}"))
        r, g, b = _parse_argb(self._settings.colour_background)
        self.modify_bg(Gtk.StateType.NORMAL, Gdk.color_parse(f"#{int(r*255):02x}{int(g*255):02x}{int(b*255):02x}"))

        self._traffic_graph.set_colours(
            *(c / 255.0 for c in ((self._settings.colour_graph_dl >> 16) & 0xFF, (self._settings.colour_graph_dl >> 8) & 0xFF, self._settings.colour_graph_dl & 0xFF)),
            *(c / 255.0 for c in ((self._settings.colour_graph_ul >> 16) & 0xFF, (self._settings.colour_graph_ul >> 8) & 0xFF, self._settings.colour_graph_ul & 0xFF)),
        )
        cr, cg, cb = (
            (self._settings.colour_graph_ping >> 16) & 0xFF,
            (self._settings.colour_graph_ping >> 8) & 0xFF,
            self._settings.colour_graph_ping & 0xFF,
        )
        self._ping_graph.set_colours(*(c / 255.0 for c in (cr, cg, cb)))
        NetworkButton._show_signal_bars = self._settings.show_signal_bars
        self._traffic_graph.set_visible(self._settings.show_speed_graph)
        self._ping_graph.set_visible(self._settings.show_ping_graph)

    def _update_swap_btn(self):
        on = self._settings.auto_swap
        color = "#00ff00" if on else "#888888"
        self._swap_btn.modify_fg(Gtk.StateType.NORMAL, Gdk.color_parse(color))
        self._swap_btn.set_tooltip_text(
            "Auto-swap: ON" if on else "Auto-swap: OFF"
        )

    def _on_swap_toggle(self, button):
        self._settings.auto_swap = not self._settings.auto_swap
        self._settings.save()
        self._update_swap_btn()

    def _on_ping_status(self, online, ms):
        GLib.idle_add(self._update_ping_ui, online, ms)

    def _update_ping_ui(self, online, ms):
        online_col = _parse_argb(self._settings.colour_online)
        offline_col = _parse_argb(self._settings.colour_offline)

        self._dot_color = online_col if online else offline_col
        self._status_dot.queue_draw()

        self._status_label.set_text("ONLINE" if online else "OFFLINE")
        if online:
            self._status_label.modify_fg(
                Gtk.StateType.NORMAL,
                Gdk.color_parse(
                    f"#{int(online_col[0]*255):02x}{int(online_col[1]*255):02x}{int(online_col[2]*255):02x}"
                ),
            )
        else:
            self._status_label.modify_fg(
                Gtk.StateType.NORMAL,
                Gdk.color_parse(
                    f"#{int(offline_col[0]*255):02x}{int(offline_col[1]*255):02x}{int(offline_col[2]*255):02x}"
                ),
            )

        if ms >= 0:
            self._ping_label.set_text(f"{int(ms)} ms")
            if ms < 50:
                self._ping_label.modify_fg(
                    Gtk.StateType.NORMAL, Gdk.color_parse("#50dc50")
                )
            elif ms < 150:
                self._ping_label.modify_fg(
                    Gtk.StateType.NORMAL, Gdk.color_parse("#dcdc3c")
                )
            else:
                self._ping_label.modify_fg(
                    Gtk.StateType.NORMAL, Gdk.color_parse("#dc503c")
                )

            if self._last_known_ping_ms == 0:
                self._ping_graph.prefill(int(ms))
            self._ping_graph.push(int(ms))
            self._last_known_ping_ms = ms
        else:
            self._ping_label.set_text("-- ms")
            self._ping_label.modify_fg(
                Gtk.StateType.NORMAL, Gdk.color_parse("#888")
            )
            push_val = 1500 if self._last_known_ping_ms > 0 else 0
            self._ping_graph.push(push_val)

        if ms >= 0 and ms < 50:
            self._ping_graph.set_primary_color(0.314, 0.784, 0.471)
        elif ms >= 0 and ms < 150:
            self._ping_graph.set_primary_color(0.863, 0.784, 0.235)
        elif ms >= 0:
            self._ping_graph.set_primary_color(0.863, 0.314, 0.235)
        else:
            self._ping_graph.set_primary_color(0.314, 0.314, 0.314)

        fails = self.ping_monitor.fail_count
        if fails > 0:
            self._fail_label.set_text(f"{fails} PING FAIL{'S' if fails > 1 else ''}")
            self._fail_label.modify_fg(
                Gtk.StateType.NORMAL, Gdk.color_parse("#dc3232")
            )
        else:
            self._fail_label.set_text("")

        if self.ping_monitor.should_swap and self._settings.auto_swap and not self._is_swapping:
            threading.Thread(target=self._try_auto_swap, daemon=True).start()

    def _on_traffic_updated(self, stats):
        GLib.idle_add(self._update_traffic_ui, stats)

    def _update_traffic_ui(self, stats):
        bits = self._settings.traffic_show_bits
        down_text = f"\u2193 {self._format_speed(stats.down_bytes_per_sec, bits)}"
        up_text = f"\u2191 {self._format_speed(stats.up_bytes_per_sec, bits)}"
        pkt_text = f"{self._format_pkts(stats.down_pkts)} / {self._format_pkts(stats.up_pkts)} pkt/s"

        self._down_lbl.set_text(down_text)
        self._up_lbl.set_text(up_text)
        self._pkt_lbl.set_text(pkt_text)

        self._traffic_graph.push(int(stats.down_bytes_per_sec), int(stats.up_bytes_per_sec))

        if stats.down_bytes_per_sec > 5_000_000:
            dc = Gdk.color_parse("#64c8ff")
        elif stats.down_bytes_per_sec > 1_000_000:
            dc = Gdk.color_parse("#50b4ff")
        else:
            dc = Gdk.color_parse("#3c82c8")
        self._down_lbl.modify_fg(Gtk.StateType.NORMAL, dc)

        if stats.up_bytes_per_sec > 2_000_000:
            uc = Gdk.color_parse("#50f064")
        elif stats.up_bytes_per_sec > 500_000:
            uc = Gdk.color_parse("#78dc78")
        else:
            uc = Gdk.color_parse("#50a050")
        self._up_lbl.modify_fg(Gtk.StateType.NORMAL, uc)

        connected_ssid = None
        for n in self._networks:
            if n.is_connected:
                connected_ssid = n.ssid
                break

        if connected_ssid != self._last_connected_ssid:
            self.bw_store.on_network_changed(connected_ssid)
            self._last_connected_ssid = connected_ssid

        if (stats.down_bytes_per_sec > 0 or stats.up_bytes_per_sec > 0) and connected_ssid:
            self.bw_store.add(connected_ssid, stats.down_bytes_per_sec, stats.up_bytes_per_sec)

    @staticmethod
    def _format_speed(bytes_per_sec, bits):
        val = bytes_per_sec * 8.0 if bits else bytes_per_sec
        kilo = 1000.0 if bits else 1024.0
        units = ["bps", "Kbps", "Mbps", "Gbps"] if bits else ["B/s", "KB/s", "MB/s", "GB/s"]
        tier = 0
        while val >= kilo and tier < len(units) - 1:
            val /= kilo
            tier += 1
        if val >= 100:
            num = f"{val:.0f}"
        elif val >= 10:
            num = f"{val:.1f}"
        else:
            num = f"{val:.2f}"
        return f"{num} {units[tier]}"

    @staticmethod
    def _format_pkts(pkts):
        if pkts >= 1000:
            return f"{pkts / 1000:.1f}k"
        return str(pkts)

    def _error_cb(self, msg):
        DebugWindow.error(msg)

    def _refresh_networks(self):
        if self._is_scanning:
            return True
        self._is_scanning = True
        threading.Thread(target=self._do_refresh, daemon=True).start()
        return True

    def _do_refresh(self):
        try:
            result = WifiManager.get_networks()
            if result.error:
                DebugWindow.warn(f"[Scan] {result.error}")
            GLib.idle_add(self._update_network_ui, result.networks, result.location_denied)
        except Exception as e:
            DebugWindow.error(f"[Scan] exception: {e}")
        finally:
            self._is_scanning = False

    def _update_network_ui(self, networks, location_denied=False):
        self._networks = networks
        width = self._network_vbox.get_allocation().width - 8 or 200
        width = max(80, width)

        visible = [
            n
            for n in networks
            if not self._settings.is_hidden(n.ssid, n.bssid)
        ]

        if location_denied:
            for c in self._network_vbox.get_children():
                self._network_vbox.remove(c)
            lbl = Gtk.Label(
                label="Location Services required.\nEnable in Privacy Settings."
            )
            lbl.set_size_request(width, 60)
            lbl.modify_fg(Gtk.StateType.NORMAL, Gdk.color_parse("orange"))
            self._network_vbox.pack_start(lbl, True, True, 0)
            self._network_vbox.show_all()
            return

        existing = {}
        for c in self._network_vbox.get_children():
            if isinstance(c, NetworkButton):
                key = f"{c.current_network.ssid}|{c.current_network.bssid}"
                existing[key] = c

        def net_key(n):
            return f"{n.ssid}|{n.bssid}"

        to_keep = {net_key(n) for n in visible}

        for key, btn in list(existing.items()):
            if key not in to_keep:
                self._network_vbox.remove(btn)

        for net in visible:
            key = net_key(net)
            if key in existing:
                existing[key].update(net, width)
            else:
                btn = NetworkButton(net, width, self.bw_store)
                btn.on_left_click = self._on_network_click
                btn.on_right_click = self._on_network_right_click
                self._network_vbox.pack_start(btn, False, False, 0)

        self._network_vbox.show_all()

    def _on_network_click(self, ssid):
        for n in self._networks:
            if n.ssid == ssid and not n.is_connected:
                DebugWindow.info(f"[Connect] clicked '{ssid}'")
                if n.is_saved:
                    threading.Thread(
                        target=lambda: self._do_connect(ssid), daemon=True
                    ).start()
                else:
                    self._show_password_dialog(n)
                return

    def _do_connect(self, ssid):
        ok = WifiManager.connect(ssid)
        if ok:
            time.sleep(2)
            GLib.idle_add(self._refresh_networks)

    def _show_password_dialog(self, network):
        dialog = Gtk.Dialog(
            title="Connect to Network",
            transient_for=self,
            flags=Gtk.DialogFlags.MODAL,
        )
        dialog.set_default_size(320, 160)

        lbl = Gtk.Label(label=f'Password for "{network.ssid}":')
        lbl.set_margin_top(12)
        lbl.set_margin_start(12)

        entry = Gtk.Entry()
        entry.set_visibility(False)
        entry.set_margin_start(12)
        entry.set_margin_end(12)
        entry.set_margin_top(8)
        entry.set_size_request(276, -1)

        show_cb = Gtk.CheckButton(label="Show password")
        show_cb.set_margin_start(12)
        show_cb.set_margin_top(4)
        show_cb.connect("toggled", lambda cb: entry.set_visibility(cb.get_active()))

        btn_connect = dialog.add_button("Connect", Gtk.ResponseType.OK)
        btn_cancel = dialog.add_button("Cancel", Gtk.ResponseType.CANCEL)

        vbox = dialog.get_content_area()
        vbox.pack_start(lbl, False, False, 0)
        vbox.pack_start(entry, False, False, 0)
        vbox.pack_start(show_cb, False, False, 0)
        dialog.show_all()

        def on_entry_activate(e):
            dialog.response(Gtk.ResponseType.OK)

        entry.connect("activate", on_entry_activate)

        response = dialog.run()
        if response == Gtk.ResponseType.OK:
            password = entry.get_text()
            dialog.destroy()
            threading.Thread(
                target=lambda: self._do_connect_with_password(network.ssid, password),
                daemon=True,
            ).start()
        else:
            dialog.destroy()

    def _do_connect_with_password(self, ssid, password):
        ok = WifiManager.connect_with_password(ssid, password)
        time.sleep(2 if ok else 0.5)
        GLib.idle_add(self._refresh_networks)
        if not ok:
            DebugWindow.error(f"[Connect] failed for '{ssid}'")
            GLib.idle_add(self._show_warning_dialog,
                          "Connection Failed",
                          "Could not connect. Check the password and try again.")

    def _show_warning_dialog(self, title, message):
        dlg = Gtk.MessageDialog(
            transient_for=self,
            flags=Gtk.DialogFlags.MODAL,
            message_type=Gtk.MessageType.WARNING,
            buttons=Gtk.ButtonsType.OK,
            text=title,
        )
        dlg.format_secondary_text(message)
        dlg.run()
        dlg.destroy()

    def _on_network_right_click(self, network, event):
        menu = Gtk.Menu()
        menu.get_style_context().add_class("dark-menu")

        def add_item(text, cb):
            item = Gtk.MenuItem(label=text)
            item.connect("activate", lambda w: cb())
            menu.append(item)

        if not network.is_connected:
            add_item(
                "Connect" if network.is_saved else "Connect (enter password)",
                lambda: self._on_network_click(network.ssid),
            )
            menu.append(Gtk.SeparatorMenuItem())

        if network.bssid:
            add_item(f'Hide this AP ({network.bssid})', lambda: self._hide_ap(network))

        add_item(f'Hide all "{network.ssid}"', lambda: self._hide_ssid(network))

        if self.bw_store.contains(network.ssid):
            menu.append(Gtk.SeparatorMenuItem())
            add_item(
                f'Clear bandwidth data for "{network.ssid}"',
                lambda: self._clear_bw(network),
            )

        menu.show_all()
        menu.popup(None, None, None, None, event.button, event.time)

    def _hide_ap(self, network):
        if network.bssid not in self._settings.hidden_bssids:
            self._settings.hidden_bssids.append(network.bssid)
        self._settings.save()
        GLib.idle_add(self._refresh_networks)

    def _hide_ssid(self, network):
        if not self._settings.is_hidden(network.ssid):
            self._settings.hidden_networks.append(network.ssid)
        self._settings.save()
        GLib.idle_add(self._refresh_networks)

    def _clear_bw(self, network):
        self.bw_store.clear(network.ssid)
        GLib.idle_add(self._refresh_networks)

    def _try_auto_swap(self):
        self._is_swapping = True
        try:
            current_ssid = WifiManager.get_connected_ssid()
            DebugWindow.info(f"[AutoSwap] triggered. Current SSID: '{current_ssid}'")

            visible_ssids = {n.ssid for n in self._networks}
            candidates = [
                n
                for n in self._settings.auto_swap_order
                if not self._settings.is_hidden(n)
                and n.lower() != (current_ssid or "").lower()
                and n in visible_ssids
            ]

            DebugWindow.info(f"[AutoSwap] candidates: {candidates}")

            if not candidates:
                fallback = [
                    n
                    for n in self._networks
                    if n.is_saved and not n.is_connected and not self._settings.is_hidden(n.ssid)
                ]
                candidates = [
                    n.ssid
                    for n in sorted(fallback, key=lambda x: -x.signal_percent)
                ]
                DebugWindow.info(f"[AutoSwap] fallback (signal): {candidates}")

            if not candidates:
                DebugWindow.warn("[AutoSwap] no candidates")
                return

            for ssid in candidates:
                DebugWindow.info(f"[AutoSwap] trying '{ssid}'")
                ok = WifiManager.connect(ssid)
                if ok:
                    time.sleep(3)
                    self.ping_monitor.reset_fail_count()
                    GLib.idle_add(lambda: self._fail_label.set_text(""))
                    play_swap_success()
                    DebugWindow.info(f"[AutoSwap] swapped to '{ssid}'")
                    GLib.idle_add(self._refresh_networks)
                    return
        except Exception as e:
            DebugWindow.error(f"[AutoSwap] {e}")
        finally:
            self._is_swapping = False

    def _open_settings(self, button):
        all_ssids = list(dict.fromkeys(n.ssid for n in self._networks))
        prev_opacity = self._settings.opacity

        def on_opacity(val):
            self.set_opacity(max(0.2, min(1.0, val / 100.0)))

        dlg = SettingsDialog(self, self._settings, all_ssids, on_opacity)
        result = dlg.run()
        dlg.destroy()

        if result != Gtk.ResponseType.OK:
            self.set_opacity(max(0.2, min(1.0, prev_opacity / 100.0)))

        self._apply_settings()
        if result == Gtk.ResponseType.OK:
            GLib.idle_add(self._refresh_networks)
        else:
            GLib.idle_add(self._refresh_networks)
