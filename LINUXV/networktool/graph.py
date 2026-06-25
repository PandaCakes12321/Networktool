import math
import time

import gi

gi.require_version("Gtk", "3.0")
from gi.repository import GLib, Gtk

SLOTS = 90
WINDOW_SECS = 90.0
LABEL_WIDTH = 36

TRAFFIC_SCALE_LEVELS = [
    10_000,
    50_000,
    100_000,
    250_000,
    500_000,
    1_000_000,
    2_500_000,
    5_000_000,
    10_000_000,
    25_000_000,
    50_000_000,
    100_000_000,
    500_000_000,
    1_000_000_000,
]

PING_SCALE_LEVELS = [25, 50, 100, 200, 500, 1000, 2000]


def format_bytes(bps):
    if bps >= 1_000_000_000:
        return f"{bps/1_000_000_000:.1f}GB/s"
    if bps >= 1_000_000:
        return f"{bps/1_000_000:.1f}MB/s"
    if bps >= 1_000:
        return f"{bps/1_000:.0f}KB/s"
    return f"{bps}B/s"


class ScrollingGraphWidget(Gtk.DrawingArea):
    def __init__(self, dual, scale_levels, formatter):
        super().__init__()
        self._dual = dual
        self._scale_levels = scale_levels
        self._fmt = formatter
        self._a = [0] * SLOTS
        self._b = [0] * SLOTS
        self._times = [0.0] * SLOTS
        self._head = 0
        self._scale_idx = 0
        self._scale = scale_levels[0]
        self._color_a = (0.235, 0.549, 0.863)
        self._color_b = (0.863, 0.314, 0.588)

        now = time.time()
        far_past = now - WINDOW_SECS * 2
        for i in range(SLOTS):
            self._times[i] = far_past

        self.set_double_buffered(False)
        self.set_size_request(-1, 50)

        self.connect("draw", self._on_draw)

        self._timer_id = GLib.timeout_add(200, self._tick)

    def set_colours(self, r, g, b, r2=0, g2=0, b2=0):
        self._color_a = (r, g, b)
        self._color_b = (r2, g2, b2)

    def set_primary_color(self, r, g, b):
        self._color_a = (r, g, b)

    def prefill(self, a_val, b_val=0):
        now = time.time()
        spacing = WINDOW_SECS / SLOTS
        for i in range(SLOTS):
            self._a[i] = a_val
            self._b[i] = b_val
            self._times[i] = now - (SLOTS - 1 - i) * spacing
        self._head = 0
        self._recalc_scale()

    def push(self, a_val, b_val=0):
        self._a[self._head] = int(a_val)
        self._b[self._head] = int(b_val)
        self._times[self._head] = time.time()
        self._head = (self._head + 1) % SLOTS
        self._recalc_scale()

    def _recalc_scale(self):
        max_val = 1
        for v in self._a:
            if v > max_val:
                max_val = v
        if self._dual:
            for v in self._b:
                if v > max_val:
                    max_val = v

        while self._scale_idx < len(self._scale_levels) - 1 and max_val > self._scale_levels[self._scale_idx] * 0.75:
            self._scale_idx += 1
        while self._scale_idx > 0 and max_val < self._scale_levels[self._scale_idx - 1] * 0.35:
            self._scale_idx -= 1

        self._scale = self._scale_levels[self._scale_idx]

    def _tick(self):
        if self.get_visible():
            self.queue_draw()
        return True

    def _on_draw(self, widget, cr):
        allocation = self.get_allocation()
        w = allocation.width
        h = allocation.height

        cr.set_source_rgb(0.047, 0.047, 0.047)
        cr.paint()

        gx = LABEL_WIDTH
        gw = max(2, w - LABEL_WIDTH)

        cr.set_source_rgb(0.274, 0.274, 0.274)
        cr.select_font_face("Sans", 0, 0)
        cr.set_font_size(8)
        cr.move_to(2, 10)
        cr.show_text(self._fmt(self._scale))

        cr.set_source_rgb(0.11, 0.11, 0.11)
        cr.set_line_width(1)
        cr.move_to(gx, h / 2)
        cr.line_to(gx + gw, h / 2)
        cr.stroke()

        cr.save()
        cr.rectangle(gx, 0, gw, h)
        cr.clip()

        if self._dual:
            self._draw_series(cr, gx, gw, h, self._b, self._color_b, 0.47, 1.2)
        self._draw_series(cr, gx, gw, h, self._a, self._color_a, 0.33, 1.5)

        window_peak = 1
        now = time.time()
        for i in range(SLOTS):
            if now - self._times[i] > WINDOW_SECS:
                continue
            v = self._a[i]
            if v > window_peak:
                window_peak = v
            if self._dual and self._b[i] > window_peak:
                window_peak = self._b[i]

        if window_peak > 1 and self._scale > 0:
            peak_y = (h - 2) * (1 - min(1.0, window_peak / self._scale)) + 1
            cr.set_source_rgba(0.824, 0.235, 0.235, 0.75)
            cr.set_dash([3, 3], 0)
            cr.set_line_width(1)
            cr.move_to(gx, peak_y)
            cr.line_to(gx + gw, peak_y)
            cr.stroke()
            cr.set_dash([], 0)

            cr.restore()
            cr.save()
            cr.rectangle(gx, 0, gw, h)
            cr.clip()

            peak_text = self._fmt(window_peak)
            cr.select_font_face("Sans", 0, 0)
            cr.set_font_size(8)
            ext = cr.text_extents(peak_text)
            ly = max(0, min(peak_y - ext.height / 2, h - ext.height))
            cr.set_source_rgba(0.824, 0.314, 0.314, 0.9)
            cr.move_to(0, ly + ext.height)
            cr.show_text(peak_text)

        cr.restore()

    def _draw_series(self, cr, gx, gw, gh, data, color, fill_alpha, line_width):
        if self._scale == 0:
            return

        px_per_sec = gw / WINDOW_SECS
        now = time.time()
        pts = []
        last_off_left = None

        for i in range(SLOTS):
            slot = (self._head - SLOTS + i + SLOTS * 2) % SLOTS
            secs_ago = now - self._times[slot]
            x = gx + gw - secs_ago * px_per_sec
            ratio = min(1.0, data[slot] / self._scale) if self._scale > 0 else 0
            y = (gh - 2) * (1 - ratio) + 1
            pt = (x, y)
            if x < gx:
                last_off_left = pt
                continue
            if last_off_left:
                pts.append(last_off_left)
                last_off_left = None
            pts.append(pt)

        if len(pts) < 2:
            return

        pts.append((gx + gw + 4, pts[-1][1]))

        cr.set_source_rgba(color[0], color[1], color[2], fill_alpha)
        cr.move_to(pts[0][0], gh)
        for px, py in pts:
            cr.line_to(px, py)
        cr.line_to(pts[-1][0], gh)
        cr.close_path()
        cr.fill()

        cr.set_source_rgb(color[0], color[1], color[2])
        cr.set_line_width(line_width)
        cr.move_to(pts[0][0], pts[0][1])
        for px, py in pts[1:]:
            cr.line_to(px, py)
        cr.stroke()
