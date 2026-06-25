import math
import struct
import threading

import gi

gi.require_version("Gst", "1.0")
from gi.repository import Gst

Gst.init(None)


def _build_wav(notes, volume):
    sample_rate = 44100
    channels = 1
    bits_per_sample = 16

    total_samples = sum(int(sample_rate * ms / 1000.0) for _, ms in notes)
    samples = []

    for hz, ms in notes:
        count = int(sample_rate * ms / 1000.0)
        for i in range(count):
            t = i / sample_rate
            fade = 1.0 - i / count
            sine = math.sin(2 * math.pi * hz * t)
            val = int(sine * fade * volume * 32767)
            val = max(-32768, min(32767, val))
            samples.append(val)

    data_size = len(samples) * 2
    buf = bytearray()

    def w(s):
        buf.extend(s)

    w(b"RIFF")
    w(struct.pack("<I", 36 + data_size))
    w(b"WAVEfmt ")
    w(struct.pack("<I", 16))
    w(struct.pack("<H", 1))
    w(struct.pack("<H", channels))
    w(struct.pack("<I", sample_rate))
    w(struct.pack("<I", sample_rate * channels * bits_per_sample // 8))
    w(struct.pack("<H", channels * bits_per_sample // 8))
    w(struct.pack("<H", bits_per_sample))
    w(b"data")
    w(struct.pack("<I", data_size))
    for s in samples:
        w(struct.pack("<h", s))

    return bytes(buf)


def _play_async(wav_bytes):
    def _play():
        try:
            pipeline = Gst.parse_launch(
                "appsrc name=src ! wavparse ! audioconvert ! autoaudiosink"
            )
            src = pipeline.get_by_name("src")
            buf = Gst.Buffer.new_wrapped(wav_bytes)
            src.emit("push-buffer", buf)
            src.emit("end-of-stream")
            pipeline.set_state(Gst.State.PLAYING)

            bus = pipeline.get_bus()
            bus.timed_pop_filtered(
                Gst.CLOCK_TIME_NONE, Gst.MessageType.EOS | Gst.MessageType.ERROR
            )
            pipeline.set_state(Gst.State.NULL)
        except Exception:
            pass

    threading.Thread(target=_play, daemon=True).start()


def play_ping_fail():
    wav = _build_wav([(660, 120), (440, 180)], 0.35)
    _play_async(wav)


def play_swap_success():
    wav = _build_wav([(523, 100), (659, 100), (784, 180)], 0.5)
    _play_async(wav)
