using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MouseBattery
{
    public class TrayApp : ApplicationContext
    {
        const string Model = "Redragon M916-ULT";
        const string ShortModel = "M916-ULT";

        readonly HiddenForm _hidden;
        readonly NotifyIcon _tray;
        readonly ContextMenuStrip _menu;
        readonly System.Windows.Forms.Timer _timer;
        readonly ToolStripMenuItem _miTitle, _miDetail, _miBattery, _miDigits, _miStartup;

        Reading _last = new Reading();
        bool _busy;
        bool _digits;
        bool _balloonPending;
        DateTime _nextPoll = DateTime.MinValue;
        string _lastSignature = "";
        bool _lowNotified, _fullNotified;

        public TrayApp()
        {
            _digits = Settings.Digits;

            _hidden = new HiddenForm();
            _hidden.DeviceChanged = delegate { _nextPoll = DateTime.MinValue; };
            IntPtr unused = _hidden.Handle; // create the handle for BeginInvoke()

            _menu = new ContextMenuStrip();
            _miTitle = new ToolStripMenuItem(Model);
            _miTitle.Enabled = false;
            _miTitle.Font = new Font(_menu.Font, FontStyle.Bold);
            _miDetail = new ToolStripMenuItem("Starting...");
            _miDetail.Enabled = false;
            _menu.Items.Add(_miTitle);
            _menu.Items.Add(_miDetail);
            _menu.Items.Add(new ToolStripSeparator());

            var refresh = new ToolStripMenuItem("Refresh now");
            refresh.Click += delegate { ForcePoll(true); };
            _menu.Items.Add(refresh);

            var styleMenu = new ToolStripMenuItem("Icon style");
            _miBattery = new ToolStripMenuItem("Battery icon");
            _miDigits = new ToolStripMenuItem("Percentage digits");
            _miBattery.Click += delegate { SetStyle(false); };
            _miDigits.Click += delegate { SetStyle(true); };
            styleMenu.DropDownItems.Add(_miBattery);
            styleMenu.DropDownItems.Add(_miDigits);
            UpdateStyleChecks();
            _menu.Items.Add(styleMenu);

            _miStartup = new ToolStripMenuItem("Start with Windows");
            _miStartup.Checked = Settings.IsStartupEnabled;
            _miStartup.Click += delegate
            {
                try { Settings.SetStartup(!Settings.IsStartupEnabled); }
                catch (Exception ex) { Log.Write("startup toggle failed: " + ex.Message); }
                _miStartup.Checked = Settings.IsStartupEnabled;
            };
            _menu.Items.Add(_miStartup);

            var copy = new ToolStripMenuItem("Copy diagnostics");
            copy.Click += delegate { CopyDiagnostics(); };
            _menu.Items.Add(copy);

            var log = new ToolStripMenuItem("Open log folder");
            log.Click += delegate
            {
                try { Process.Start("explorer.exe", "\"" + Log.Dir + "\""); } catch { }
            };
            _menu.Items.Add(log);
            _menu.Items.Add(new ToolStripSeparator());

            var exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { ExitApp(); };
            _menu.Items.Add(exit);

            _tray = new NotifyIcon();
            _tray.ContextMenuStrip = _menu;
            _tray.Text = ShortModel + " - starting...";
            _tray.Visible = true;
            _tray.MouseClick += OnIconClick;
            SetIcon();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { Tick(); };
            _timer.Start();

            Log.Write("started " + Application.ExecutablePath);
            ForcePoll(false);
        }

        void OnIconClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ForcePoll(true);
        }

        void Tick()
        {
            if (_busy || DateTime.UtcNow < _nextPoll) return;
            Poll();
        }

        void ForcePoll(bool balloon)
        {
            _balloonPending = _balloonPending || balloon;
            _nextPoll = DateTime.MinValue;
            if (!_busy) Poll();
        }

        void Poll()
        {
            _busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Reading reading;
                try { reading = HidReader.Read(); }
                catch (Exception ex)
                {
                    reading = Reading.Fail(ex.Message);
                    Log.Write("poll error: " + ex);
                }
                try
                {
                    _hidden.BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        Apply(reading);
                    });
                }
                catch { _busy = false; }
            });
        }

        void Apply(Reading r)
        {
            _last = r;

            string signature = r.Known
                ? r.Percent + "%/" + (r.Wired ? "wired" : "wireless") + "/" + r.Millivolts
                : "unknown:" + r.Error;
            if (signature != _lastSignature)
            {
                _lastSignature = signature;
                Log.Write(signature + (r.Raw.Length > 0 ? "  raw " + r.Raw : ""));
            }

            UpdateUi();

            if (_balloonPending)
            {
                _balloonPending = false;
                ShowBatteryBalloon(r);
            }
            NotifyEvents(r);

            if (r.Known && r.Wired) _nextPoll = DateTime.UtcNow.AddSeconds(5);
            else if (r.Known) _nextPoll = DateTime.UtcNow.AddSeconds(30);
            else _nextPoll = DateTime.UtcNow.AddSeconds(8);
        }

        void NotifyEvents(Reading r)
        {
            if (!r.Known)
            {
                if (!r.Found) _lowNotified = false;
                return;
            }
            if (r.Wired)
            {
                _lowNotified = false;
                if (r.Percent >= 100 && !_fullNotified)
                {
                    _fullNotified = true;
                    _tray.ShowBalloonTip(5000, "Mouse fully charged",
                        Model + " is at 100%.", ToolTipIcon.Info);
                }
                if (r.Percent < 100) _fullNotified = false;
                return;
            }
            _fullNotified = false;
            if (r.Percent <= 15 && !_lowNotified)
            {
                _lowNotified = true;
                _tray.ShowBalloonTip(6000, "Mouse battery low",
                    Model + " is at " + r.Percent + "%. Time to charge.", ToolTipIcon.Warning);
            }
            if (r.Percent >= 25) _lowNotified = false;
        }

        void ShowBatteryBalloon(Reading r)
        {
            if (r.Known)
            {
                string status = r.Wired ? (r.Percent >= 100 ? "Fully charged" : "Charging (cable connected)") : "Wireless (2.4 GHz)";
                _tray.ShowBalloonTip(5000, Model,
                    "Battery: " + r.Percent + "%\r\nStatus: " + status + "\r\nCell: " + r.Volts,
                    ToolTipIcon.None);
            }
            else
            {
                _tray.ShowBalloonTip(5000, Model,
                    r.Found
                        ? "The receiver is connected, but the mouse is not replying (asleep or switched off)."
                        : "No supported mouse found. Connect the 2.4 GHz receiver or the USB cable.",
                    ToolTipIcon.None);
            }
        }

        void SetStyle(bool digits)
        {
            if (_digits == digits)
            {
                UpdateStyleChecks();
                Log.Write("style refresh -> " + (digits ? "digits" : "battery"));
                UpdateUi(true);
                return;
            }
            _digits = digits;
            try { Settings.Digits = _digits; }
            catch (Exception ex) { Log.Write("style save failed: " + ex.Message); }
            UpdateStyleChecks();
            Log.Write("style -> " + (digits ? "digits" : "battery"));
            UpdateUi(true);
        }

        void UpdateStyleChecks()
        {
            _miBattery.Checked = !_digits;
            _miDigits.Checked = _digits;
        }

        void UpdateUi(bool forceIconRefresh = false)
        {
            Reading r = _last;
            string text;
            string detail;

            if (r.Known)
            {
                string mode = r.Wired ? (r.Percent >= 100 ? "full" : "charging") : "wireless";
                text = ShortModel + " " + r.Percent + "% " + mode + " - " + r.Volts;
                detail = r.Percent + "% - " + mode + " - " + r.Volts;
            }
            else if (r.Found)
            {
                text = ShortModel + " - asleep / no reply";
                detail = "No reply (mouse asleep or off)";
            }
            else
            {
                text = ShortModel + " - not connected";
                detail = "Not connected";
            }

            if (text.Length > 63) text = text.Substring(0, 63);
            _tray.Text = text;
            _miDetail.Text = detail;
            SetIcon(forceIconRefresh);
        }

        void SetIcon(bool force = false)
        {
            try
            {
                Icon icon = IconFactory.Build(_last, _digits, Theme.IsLightTaskbar());
                Icon old = _tray.Icon;
                if (force && _tray.Visible)
                {
                    // Delete and re-add the shell icon; NIM_MODIFY alone sometimes leaves
                    // the previous image on screen until the tray is repainted.
                    _tray.Visible = false;
                    _tray.Icon = icon;
                    _tray.Visible = true;
                }
                else
                {
                    _tray.Icon = icon;
                }
                if (old != null) old.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("icon error: " + ex.Message);
            }
        }

        void CopyDiagnostics()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string text;
                try { text = HidReader.Diagnostics(); }
                catch (Exception ex) { text = "diagnostics failed: " + ex; }
                try
                {
                    _hidden.BeginInvoke((MethodInvoker)delegate
                    {
                        try
                        {
                            Clipboard.SetText(text);
                            _tray.ShowBalloonTip(3000, "Diagnostics copied", "The report is on your clipboard.", ToolTipIcon.None);
                        }
                        catch { }
                    });
                }
                catch { }
            });
        }

        void ExitApp()
        {
            _timer.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            _hidden.Dispose();
            Log.Write("exited");
            ExitThread();
        }

        class HiddenForm : Form
        {
            public Action DeviceChanged;

            public HiddenForm()
            {
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-32000, -32000);
                Size = new Size(1, 1);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0219 && DeviceChanged != null) DeviceChanged();
                base.WndProc(ref m);
            }
        }
    }
}
