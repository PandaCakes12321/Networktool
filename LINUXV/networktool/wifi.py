import time
import uuid

import dbus


NM_SERVICE = "org.freedesktop.NetworkManager"
NM_PATH = "/org/freedesktop/NetworkManager"
NM_IFACE = "org.freedesktop.NetworkManager"
NM_DEVICE_IFACE = "org.freedesktop.NetworkManager.Device"
NM_WIRELESS_IFACE = "org.freedesktop.NetworkManager.Device.Wireless"
NM_AP_IFACE = "org.freedesktop.NetworkManager.AccessPoint"
NM_SETTINGS_IFACE = "org.freedesktop.NetworkManager.Settings"
NM_CONNECTION_IFACE = "org.freedesktop.NetworkManager.Settings.Connection"
NM_ACTIVE_CONNECTION_IFACE = "org.freedesktop.NetworkManager.Connection.Active"
DBUS_PROPS = "org.freedesktop.DBus.Properties"

NM_DEVICE_TYPE_WIFI = 2


class WifiNetwork:
    def __init__(self, ssid="", bssid="", signal=0, connected=False, saved=False, has_internet=True):
        self.ssid = ssid
        self.bssid = bssid
        self.signal_percent = signal
        self.is_connected = connected
        self.is_saved = saved
        self.has_internet = has_internet


class WifiScanResult:
    def __init__(self):
        self.networks = []
        self.location_denied = False
        self.error = None


class WifiManager:
    _bus = None

    @classmethod
    def _get_bus(cls):
        if cls._bus is None:
            cls._bus = dbus.SystemBus()
        return cls._bus

    @classmethod
    def _get_nm(cls):
        bus = cls._get_bus()
        obj = bus.get_object(NM_SERVICE, NM_PATH)
        return dbus.Interface(obj, NM_IFACE)

    @classmethod
    def _get_props(cls, service, path):
        bus = cls._get_bus()
        obj = bus.get_object(service, path)
        return dbus.Interface(obj, DBUS_PROPS)

    @classmethod
    def _get_wifi_device(cls):
        nm = cls._get_nm()
        devices = nm.GetDevices()
        for dev_path in devices:
            props = cls._get_props(NM_SERVICE, dev_path)
            dev_type = props.Get(NM_DEVICE_IFACE, "DeviceType")
            if dev_type == NM_DEVICE_TYPE_WIFI:
                return dev_path
        return None

    @classmethod
    def get_networks(cls):
        result = WifiScanResult()
        try:
            wifi_dev = cls._get_wifi_device()
            if not wifi_dev:
                result.error = "No WiFi device found"
                return result

            wifi_props = cls._get_props(NM_SERVICE, wifi_dev)
            wifi_iface = dbus.Interface(
                cls._get_bus().get_object(NM_SERVICE, wifi_dev), NM_WIRELESS_IFACE
            )

            try:
                wifi_iface.RequestScan(dbus.Dictionary({}, signature="sv"))
            except Exception:
                pass

            time.sleep(0.5)

            try:
                ap_paths = wifi_props.Get(NM_WIRELESS_IFACE, "AccessPoints")
            except Exception:
                ap_paths = wifi_iface.GetAccessPoints()

            ap_paths = list(ap_paths)

            try:
                active_ap_path = wifi_props.Get(NM_WIRELESS_IFACE, "ActiveAccessPoint")
            except Exception:
                active_ap_path = "/"

            try:
                interface_name = wifi_props.Get(NM_DEVICE_IFACE, "Interface")
            except Exception:
                interface_name = ""

            connected_ssid = cls.get_connected_ssid()
            saved_ssids = cls.get_saved_profiles()

            bssid_map = {}
            for ap_path in ap_paths:
                try:
                    ap_props = cls._get_props(NM_SERVICE, ap_path)
                    ssid_bytes = ap_props.Get(NM_AP_IFACE, "Ssid")
                    ssid = bytes(ssid_bytes).decode("utf-8", errors="replace")
                    bssid = ap_props.Get(NM_AP_IFACE, "Bssid").upper()
                    strength = ap_props.Get(NM_AP_IFACE, "Strength")
                    strength = int(strength)

                    is_connected = ap_path == active_ap_path
                    is_saved = any(s.lower() == ssid.lower() for s in saved_ssids)

                    key = f"{ssid}|{bssid}"
                    bssid_map[key] = WifiNetwork(
                        ssid=ssid,
                        bssid=bssid,
                        signal=strength,
                        connected=is_connected,
                        saved=is_saved,
                        has_internet=True,
                    )
                except Exception:
                    continue

            for net in bssid_map.values():
                result.networks.append(net)

            result.networks.sort(
                key=lambda n: (
                    not n.is_connected,
                    not n.is_saved,
                    n.ssid.lower(),
                )
            )

        except Exception as e:
            result.error = str(e)

        return result

    @classmethod
    def get_connected_ssid(cls):
        try:
            wifi_dev = cls._get_wifi_device()
            if not wifi_dev:
                return ""
            wifi_props = cls._get_props(NM_SERVICE, wifi_dev)
            active_ap_path = wifi_props.Get(NM_WIRELESS_IFACE, "ActiveAccessPoint")
            if not active_ap_path or active_ap_path == "/":
                return ""
            ap_props = cls._get_props(NM_SERVICE, active_ap_path)
            ssid_bytes = ap_props.Get(NM_AP_IFACE, "Ssid")
            return bytes(ssid_bytes).decode("utf-8", errors="replace")
        except Exception:
            return ""

    @classmethod
    def get_saved_profiles(cls):
        try:
            bus = cls._get_bus()
            settings_obj = bus.get_object(NM_SERVICE, "/org/freedesktop/NetworkManager/Settings")
            settings = dbus.Interface(settings_obj, NM_SETTINGS_IFACE)
            conn_paths = settings.ListConnections()
            profiles = []
            for cp in conn_paths:
                try:
                    conn = dbus.Interface(
                        bus.get_object(NM_SERVICE, cp), NM_CONNECTION_IFACE
                    )
                    settings_dict = conn.GetSettings()
                    conn_id = settings_dict.get("connection", {}).get("id", "")
                    conn_type = settings_dict.get("connection", {}).get("type", "")
                    if conn_type == "802-11-wireless" and conn_id:
                        profiles.append(conn_id)
                except Exception:
                    continue
            return profiles
        except Exception:
            return []

    @classmethod
    def connect(cls, ssid):
        try:
            wifi_dev = cls._get_wifi_device()
            if not wifi_dev:
                return False
            nm = cls._get_nm()

            connection = {
                "connection": {
                    "type": "802-11-wireless",
                    "uuid": str(uuid.uuid4()),
                    "id": ssid,
                },
                "802-11-wireless": {
                    "ssid": dbus.ByteArray(ssid.encode("utf-8")),
                    "mode": "infrastructure",
                },
            }

            nm.AddAndActivateConnection(
                dbus.Dictionary(connection, signature="sa{sv}"),
                wifi_dev,
                dbus.ObjectPath("/"),
            )
            return True
        except Exception:
            return False

    @classmethod
    def connect_with_password(cls, ssid, password):
        try:
            bus = cls._get_bus()
            wifi_dev = cls._get_wifi_device()
            if not wifi_dev:
                return False
            nm = cls._get_nm()

            connection = {
                "connection": {
                    "type": "802-11-wireless",
                    "uuid": str(uuid.uuid4()),
                    "id": ssid,
                },
                "802-11-wireless": {
                    "ssid": dbus.ByteArray(ssid.encode("utf-8")),
                    "mode": "infrastructure",
                    "security": "802-11-wireless-security",
                },
                "802-11-wireless-security": {
                    "key-mgmt": "wpa-psk",
                    "auth-alg": "open",
                    "psk": password,
                },
            }

            nm.AddAndActivateConnection(
                dbus.Dictionary(connection, signature="sa{sv}"),
                wifi_dev,
                dbus.ObjectPath("/"),
            )
            return True
        except Exception:
            return False
