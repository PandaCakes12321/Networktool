#!/usr/bin/env python3
import fcntl
import os
import signal
import sys

LOCK_PATH = "/tmp/networktool.lock"


def main():
    lock_fd = _acquire_lock()
    if lock_fd is None:
        return

    signal.signal(signal.SIGUSR1, lambda s, f: _show_window())

    import gi

    gi.require_version("Gtk", "3.0")
    gi.require_version("Gdk", "3.0")
    from gi.repository import Gtk, GLib

    GLib.unix_signal_add(GLib.PRIORITY_DEFAULT, signal.SIGUSR1, _show_window)

    from networktool.main_window import MainWindow

    win = MainWindow()
    Gtk.main()


def _acquire_lock():
    try:
        fd = os.open(LOCK_PATH, os.O_CREAT | os.O_RDWR, 0o644)
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        os.truncate(fd, 0)
        os.write(fd, str(os.getpid()).encode())
        return fd
    except (IOError, OSError):
        try:
            with open(LOCK_PATH) as f:
                pid = int(f.read().strip())
            os.kill(pid, signal.SIGUSR1)
        except Exception:
            pass
        return None


def _show_window():
    import gi

    gi.require_version("Gtk", "3.0")
    from gi.repository import Gtk

    for w in Gtk.window_list_top_levels():
        if "Networktool" in (w.get_title() or ""):
            w.show_all()
            w.present()
            w.deiconify()
            return


if __name__ == "__main__":
    main()
