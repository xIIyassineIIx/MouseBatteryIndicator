using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MouseBattery
{
    public class Reading
    {
        public bool Found;          // a matching HID collection exists
        public bool Known;          // a valid battery reply was parsed
        public int Percent = -1;
        public bool Wired;          // reply byte 7: cable connected
        public int Millivolts = -1;
        public ushort Pid;
        public string Product = "";
        public string Raw = "";
        public string Error = "";

        public bool Charging { get { return Known && Wired && Percent < 100; } }
        public bool Full { get { return Known && Wired && Percent >= 100; } }
        public string Volts { get { return Millivolts > 0 ? (Millivolts / 1000.0).ToString("0.00") + " V" : "? V"; } }

        public static Reading Fail(string error)
        {
            return new Reading { Found = false, Known = false, Error = error };
        }
    }

    public class HidDeviceInfo
    {
        public string Path;
        public ushort Vid, Pid;
        public ushort Usage, UsagePage;
        public int InLen, OutLen, FeatureLen;
        public string Product = "";
        public int Score;
    }

    /// <summary>
    /// Reads the battery of Compx-based Redragon mice (VID 0x3554) using the
    /// shared 17-byte report-0x08 protocol. Verified on an M916-ULT (F54D).
    /// </summary>
    public static class HidReader
    {
        public const ushort TargetVid = 0x3554;
        public const ushort BatteryUsagePage = 0xFF02;
        public const ushort BatteryUsage = 0x0002;

        // ---------- Win32 / HID ----------
        const int DIGCF_PRESENT = 0x02;
        const int DIGCF_DEVICEINTERFACE = 0x10;
        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 0x01;
        const uint FILE_SHARE_WRITE = 0x02;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        const int ERROR_IO_PENDING = 997;

        [StructLayout(LayoutKind.Sequential)]
        struct GUID { public uint Data1; public ushort Data2; public ushort Data3; public byte D4, D5, D6, D7, D8, D9, D10, D11; }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public GUID InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
            public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out GUID g);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr d);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr d);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr d, ref HIDP_CAPS c);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(IntPtr h, StringBuilder s, int n);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref GUID g, IntPtr e, IntPtr p, int f);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr di, ref GUID g, int i, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA d, IntPtr det, int sz, out int req, IntPtr di);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateFile(string n, uint a, uint sh, IntPtr sec, uint disp, uint fl, IntPtr t);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(IntPtr h, byte[] b, int n, out int w, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(IntPtr h, byte[] b, int n, out int r, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetOverlappedResult(IntPtr h, IntPtr ov, out int transferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CancelIoEx(IntPtr h, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateEvent(IntPtr sec, bool manual, bool initial, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ResetEvent(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr h, uint ms);

        static readonly IntPtr Invalid = new IntPtr(-1);

        static IntPtr Open(string path, bool overlapped)
        {
            return CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, overlapped ? FILE_FLAG_OVERLAPPED : 0, IntPtr.Zero);
        }

        public static List<HidDeviceInfo> Enumerate()
        {
            var list = new List<HidDeviceInfo>();
            GUID guid;
            HidD_GetHidGuid(out guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == Invalid) return list;
            var did = new SP_DEVICE_INTERFACE_DATA();
            did.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
            for (int i = 0; ; i++)
            {
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did)) break;
                int need;
                SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out need, IntPtr.Zero);
                if (need <= 0) continue;
                IntPtr mem = Marshal.AllocHGlobal(need);
                try
                {
                    Marshal.WriteInt32(mem, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref did, mem, need, out need, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringUni(new IntPtr(mem.ToInt64() + 4));
                    IntPtr h = Open(path, false);
                    if (h == Invalid) continue;
                    try
                    {
                        var dev = new HidDeviceInfo { Path = path };
                        var attr = new HIDD_ATTRIBUTES();
                        attr.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                        if (HidD_GetAttributes(h, ref attr)) { dev.Vid = attr.VendorID; dev.Pid = attr.ProductID; }
                        if (dev.Vid != TargetVid) continue;
                        IntPtr pp;
                        if (HidD_GetPreparsedData(h, out pp))
                        {
                            var caps = new HIDP_CAPS();
                            if (HidP_GetCaps(pp, ref caps) >= 0)
                            {
                                dev.Usage = caps.Usage; dev.UsagePage = caps.UsagePage;
                                dev.InLen = caps.InputReportByteLength;
                                dev.OutLen = caps.OutputReportByteLength;
                                dev.FeatureLen = caps.FeatureReportByteLength;
                            }
                            HidD_FreePreparsedData(pp);
                        }
                        var sb = new StringBuilder(260);
                        if (HidD_GetProductString(h, sb, sb.Capacity * 2)) dev.Product = sb.ToString();
                        dev.Score = Score(dev);
                        list.Add(dev);
                    }
                    finally { CloseHandle(h); }
                }
                finally { Marshal.FreeHGlobal(mem); }
            }
            SetupDiDestroyDeviceInfoList(set);
            return list;
        }

        static int Score(HidDeviceInfo d)
        {
            int s = 0;
            if (d.Pid == 0xF54D) s += 300;       // M916-ULT wired mode
            else if (d.Pid == 0xF54F) s += 200;  // M916-ULT 2.4 GHz receiver
            else s += 50;
            if (d.Product.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) >= 0) s += 10;
            return s;
        }

        static void PrepOverlapped(IntPtr ov, IntPtr ev)
        {
            for (int i = 0; i < 32; i++) Marshal.WriteByte(ov, i, 0);
            int eventOffset = IntPtr.Size == 8 ? 24 : 12;
            Marshal.WriteIntPtr(new IntPtr(ov.ToInt64() + eventOffset), ev);
        }

        static bool DoIo(bool write, IntPtr h, byte[] buf, IntPtr ov, IntPtr ev, int timeoutMs, out int transferred, out string error)
        {
            transferred = 0;
            error = null;
            ResetEvent(ev);
            PrepOverlapped(ov, ev);
            bool ok = write
                ? WriteFile(h, buf, buf.Length, out transferred, ov)
                : ReadFile(h, buf, buf.Length, out transferred, ov);
            if (ok) return true;
            int err = Marshal.GetLastWin32Error();
            if (err != ERROR_IO_PENDING) { error = "error " + err; return false; }
            uint w = WaitForSingleObject(ev, (uint)timeoutMs);
            if (w != 0)
            {
                CancelIoEx(h, ov);
                error = "timeout";
                return false;
            }
            if (!GetOverlappedResult(h, ov, out transferred, false))
            {
                error = "io failed " + Marshal.GetLastWin32Error();
                return false;
            }
            return true;
        }

        static bool Query(HidDeviceInfo dev, out byte[] reply, out string error)
        {
            reply = null;
            error = null;
            IntPtr h = Open(dev.Path, true);
            if (h == Invalid) { error = "open failed " + Marshal.GetLastWin32Error(); return false; }
            IntPtr ev = CreateEvent(IntPtr.Zero, true, false, null);
            IntPtr ov = Marshal.AllocHGlobal(32);
            try
            {
                int outLen = Math.Max(17, dev.OutLen);
                var req = new byte[outLen];
                req[0] = 8; req[1] = 4; req[16] = 73; // checksum: all 17 bytes sum to 0x55
                int written;
                if (!DoIo(true, h, req, ov, ev, 900, out written, out error)) return false;
                if (written != req.Length) { error = "short write " + written; return false; }

                int inLen = Math.Max(17, dev.InLen);
                var buf = new byte[inLen];
                int read;
                if (!DoIo(false, h, buf, ov, ev, 1300, out read, out error)) return false;
                if (read < 10) { error = "short reply " + read; return false; }
                reply = buf;
                return true;
            }
            finally
            {
                CloseHandle(h);
                CloseHandle(ev);
                Marshal.FreeHGlobal(ov);
            }
        }

        static string Hex(byte[] b, int n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n && i < b.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(b[i].ToString("X2"));
            }
            return sb.ToString();
        }

        public static Reading Read()
        {
            List<HidDeviceInfo> all = Enumerate();
            var candidates = new List<HidDeviceInfo>();
            foreach (var d in all)
            {
                if (d.UsagePage == BatteryUsagePage && d.Usage == BatteryUsage && d.InLen >= 10 && d.OutLen >= 17)
                    candidates.Add(d);
            }
            if (candidates.Count == 0)
                return new Reading { Found = false, Known = false, Error = "no battery interface (mouse not connected)" };

            candidates.Sort(delegate(HidDeviceInfo a, HidDeviceInfo b) { return b.Score.CompareTo(a.Score); });

            Reading last = null;
            foreach (var dev in candidates)
            {
                byte[] reply;
                string err;
                if (!Query(dev, out reply, out err))
                {
                    last = new Reading { Found = true, Known = false, Pid = dev.Pid, Product = dev.Product, Error = err };
                    continue;
                }
                if (reply[0] != 8 || reply[1] != 4)
                {
                    last = new Reading { Found = true, Known = false, Pid = dev.Pid, Product = dev.Product, Error = "unexpected reply", Raw = Hex(reply, 17) };
                    continue;
                }
                int pct = reply[6];
                if (pct < 0 || pct > 100)
                {
                    last = new Reading { Found = true, Known = false, Pid = dev.Pid, Product = dev.Product, Error = "bad percent " + pct, Raw = Hex(reply, 17) };
                    continue;
                }
                return new Reading
                {
                    Found = true,
                    Known = true,
                    Percent = pct,
                    Wired = reply[7] != 0,
                    Millivolts = (reply[8] << 8) | reply[9],
                    Pid = dev.Pid,
                    Product = dev.Product,
                    Raw = Hex(reply, 17)
                };
            }
            return last ?? new Reading { Found = true, Known = false, Error = "no reply" };
        }

        public static string Diagnostics()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Redragon M916-ULT battery diagnostics");
            sb.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("HID devices (VID " + TargetVid.ToString("X4") + "):");
            foreach (var d in Enumerate())
            {
                sb.AppendFormat("  PID={0:X4} usage={1:X4}:{2:X4} in={3} out={4} feat={5} '{6}'{7}",
                    d.Pid, d.UsagePage, d.Usage, d.InLen, d.OutLen, d.FeatureLen, d.Product, Environment.NewLine);
                sb.AppendLine("    " + d.Path);
            }
            sb.AppendLine();
            Reading r = Read();
            if (r.Known)
            {
                sb.AppendFormat("Reading: {0}%  {1}  {2} mV  PID {3:X4}{4}",
                    r.Percent, r.Wired ? "wired/charging" : "wireless", r.Millivolts, r.Pid, Environment.NewLine);
            }
            else
            {
                sb.AppendLine("Reading: unavailable (" + r.Error + ")");
            }
            if (r.Raw.Length > 0) sb.AppendLine("Raw: " + r.Raw);
            return sb.ToString();
        }
    }
}
