using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LifeBalance
{
    internal static class Program
    {
        internal static readonly string Home = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LifeBalance");

        /// <summary>数据根目录。默认在 %LOCALAPPDATA%\LifeBalance；
        /// 传 --data &lt;目录&gt; 可以换成别处（做实验或做便携版用）。</summary>
        internal static string Data
        {
            get
            {
                if (_data != null) { return _data; }
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], "--data", StringComparison.OrdinalIgnoreCase))
                    {
                        _data = Path.GetFullPath(args[i + 1]);
                        return _data;
                    }
                }
                _data = Home;
                return _data;
            }
        }

        private static string _data;

        internal static string LogFile
        {
            get { return Path.Combine(Data, "log.txt"); }
        }

        internal static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogFile,
                    DateTime.Now.ToString("MM-dd HH:mm:ss") + "  " + message + "\r\n");
            }
            catch { /* 记不上就算了 */ }
        }

        [STAThread]
        private static void Main()
        {
            try { Directory.CreateDirectory(Home); } catch { /* 忽略 */ }
            try { Directory.CreateDirectory(Data); } catch { /* 忽略 */ }
            try { File.Delete(LogFile); } catch { /* 忽略 */ }
            Log("=== 启动 ===  数据目录 = " + Data);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ShellForm());
            Log("=== 退出 ===");
        }
    }

    internal sealed class ShellForm : Form
    {
        private const string Host = "app.local";

        private readonly WebView2 _view = new WebView2();
        private readonly string _www;
        private readonly string _stateFile = Path.Combine(Program.Data, "window.txt");

        private NotifyIcon _tray;
        private ToolStripMenuItem _topItem;
        private bool _quitting;
        private bool _ready;

        /// <summary>
        /// 找界面文件。优先用 exe 同目录的 www（改了立刻生效，不用重新编译）；
        /// 没有就说明是单文件分发版，把嵌在程序里的那份释放到数据目录来用。
        /// </summary>
        private static string ResolveWebRoot()
        {
            string beside = Path.Combine(AppContext.BaseDirectory, "www");
            if (File.Exists(Path.Combine(beside, "index.html")))
            {
                Program.Log("界面文件：用 exe 同目录的 www（可随时修改）");
                return beside;
            }

            string dir = Path.Combine(Program.Data, "www");
            string[] names = { "index.html" };
            try
            {
                Directory.CreateDirectory(dir);
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (string name in names)
                {
                    using (Stream src = asm.GetManifestResourceStream("www." + name))
                    {
                        if (src == null) { continue; }
                        using (var dst = new FileStream(Path.Combine(dir, name),
                                   FileMode.Create, FileAccess.Write))
                        {
                            src.CopyTo(dst);
                        }
                    }
                }
                Program.Log("界面文件：已从程序内部释放到 " + dir);
                return dir;
            }
            catch (Exception ex)
            {
                Program.Log("释放界面文件失败: " + ex.Message);
                return beside;
            }
        }

        internal ShellForm()
        {
            _www = ResolveWebRoot();

            Text = "人生余额";
            BackColor = Color.White;
            MinimumSize = new Size(380, 360);
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Icon = BuildIcon();
            LoadWindowBounds();

            _view.Dock = DockStyle.Fill;
            _view.DefaultBackgroundColor = Color.White;
            Controls.Add(_view);

            BuildTray();

            Load += async (s, e) => await StartAsync();
            FormClosing += OnFormClosing;
            Resize += OnResize;
        }

        /* ------------------------------------------------ 启动 */

        private async Task StartAsync()
        {
            Program.Log("www 目录 = " + _www);
            if (!File.Exists(Path.Combine(_www, "index.html")))
            {
                Program.Log("找不到 www\\index.html，中止");
                MessageBox.Show(
                    "找不到 www\\index.html。\r\n\r\n请确认 index.html 和这个程序放在同一个目录下的 www 文件夹里。",
                    "人生余额", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Program.Log("www\\index.html 存在");

            try
            {
                string dataDir = Path.Combine(Program.Data, "webview");
                Program.Log("WebView2 数据目录 = " + dataDir);

                // 这台机器上 WebView2 的 GPU 进程会反复崩溃（大概率是显卡驱动 /
                // 游戏反作弊 / 覆盖层抢 GPU），崩到最后浏览器进程直接退出、窗口一片白。
                // 界面本身很轻，走软件渲染完全够用，换来的是稳定。
                string flags = "--disable-gpu";
                if (Environment.GetEnvironmentVariable("LIFEBALANCE_GPU") == "1")
                {
                    flags = null;
                    Program.Log("（按环境变量要求，启用 GPU 加速）");
                }
                if (Environment.GetEnvironmentVariable("LIFEBALANCE_NOSANDBOX") == "1")
                {
                    flags = (flags == null ? "" : flags + " ") + "--no-sandbox";
                }
                var options = new CoreWebView2EnvironmentOptions(flags);
                Program.Log("启动参数 = " + (flags ?? "(默认)"));
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(
                    null, dataDir, options);
                Program.Log("环境已创建，版本 = " + env.BrowserVersionString);
                await _view.EnsureCoreWebView2Async(env);
                Program.Log("WebView2 控制器就绪");
            }
            catch (Exception ex)
            {
                Program.Log("初始化失败: " + ex.GetType().Name + " " + ex.Message);
                MessageBox.Show(
                    "浏览器内核启动失败。\r\n\r\n这台机器需要 Microsoft Edge WebView2 运行时，可以从微软官网免费下载安装。\r\n\r\n" + ex.Message,
                    "人生余额", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            CoreWebView2 core = _view.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreDevToolsEnabled = true;

            core.SetVirtualHostNameToFolderMapping(
                Host, _www, CoreWebView2HostResourceAccessKind.Allow);
            Program.Log("虚拟域名已映射 " + Host + " -> " + _www);

            core.NavigationCompleted += (s, e) =>
            {
                Program.Log("导航完成 成功=" + e.IsSuccess +
                            (e.IsSuccess ? "" : " 错误=" + e.WebErrorStatus));
            };
            core.ProcessFailed += (s, e) =>
            {
                Program.Log("浏览器进程异常: " + e.ProcessFailedKind);
            };
            _ready = true;
            Navigate(widget: true);
        }

        private void Navigate(bool widget)
        {
            if (!_ready) { return; }
            _view.CoreWebView2.Navigate(
                "https://" + Host + "/index.html" + (widget ? "?widget=1" : ""));
        }

        /* ------------------------------------------------ 托盘 */

        private void BuildTray()
        {
            var menu = new ContextMenuStrip();

            menu.Items.Add("显示 / 隐藏窗口", null, (s, e) => ToggleVisible());

            _topItem = new ToolStripMenuItem("始终置顶") { Checked = TopMost, CheckOnClick = true };
            _topItem.Click += (s, e) => { TopMost = _topItem.Checked; };
            menu.Items.Add(_topItem);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("打开小窗", null, (s, e) => { Show(); Navigate(true); });
            menu.Items.Add("打开总览页", null, (s, e) => { Show(); Navigate(false); });
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("退出", null, (s, e) =>
            {
                _quitting = true;
                Close();
            });

            _tray = new NotifyIcon
            {
                Icon = BuildIcon(),
                Text = "人生余额",
                Visible = true,
                ContextMenuStrip = menu
            };
            _tray.DoubleClick += (s, e) => ToggleVisible();
        }

        private void ToggleVisible()
        {
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                Hide();
            }
            else
            {
                Show();
                WindowState = FormWindowState.Normal;
                Activate();
            }
        }

        private void OnResize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized) { Hide(); }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            SaveBounds();

            if (!_quitting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _tray.ShowBalloonTip(1500, "人生余额", "还在托盘里跑着，双击图标叫回来。",
                    ToolTipIcon.None);
                return;
            }

            _tray.Visible = false;
            _tray.Dispose();
        }

        /* ------------------------------------------------ 窗口位置 */

        private void LoadWindowBounds()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            var target = new Rectangle(0, 0, 540, 620);
            bool restored = false;

            if (File.Exists(_stateFile))
            {
                try
                {
                    string[] p = File.ReadAllText(_stateFile).Split(',');
                    if (p.Length == 4)
                    {
                        target = new Rectangle(
                            int.Parse(p[0], CultureInfo.InvariantCulture),
                            int.Parse(p[1], CultureInfo.InvariantCulture),
                            int.Parse(p[2], CultureInfo.InvariantCulture),
                            int.Parse(p[3], CultureInfo.InvariantCulture));
                        restored = true;
                    }
                }
                catch { /* 坏文件就当没存过 */ }
            }

            if (target.Width < MinimumSize.Width) { target.Width = MinimumSize.Width; }
            if (target.Height < MinimumSize.Height) { target.Height = MinimumSize.Height; }
            if (target.Width > wa.Width) { target.Width = wa.Width; }
            if (target.Height > wa.Height) { target.Height = wa.Height; }

            if (!restored)
            {
                target.X = wa.Right - target.Width - 40;   // 首次运行：贴右上角
                target.Y = wa.Top + 40;
            }

            // 换了屏幕 / 改过分辨率之后，别把窗口留在看不见的地方
            if (target.Right > wa.Right) { target.X = wa.Right - target.Width - 40; }
            if (target.Bottom > wa.Bottom) { target.Y = wa.Top + 40; }
            if (target.Left < wa.Left) { target.X = wa.Left + 40; }
            if (target.Top < wa.Top) { target.Y = wa.Top + 40; }

            Bounds = target;
        }

        private void SaveBounds()
        {
            try
            {
                Rectangle b = (WindowState == FormWindowState.Normal) ? Bounds : RestoreBounds;
                File.WriteAllText(_stateFile, string.Join(",",
                    b.Left.ToString(CultureInfo.InvariantCulture),
                    b.Top.ToString(CultureInfo.InvariantCulture),
                    b.Width.ToString(CultureInfo.InvariantCulture),
                    b.Height.ToString(CultureInfo.InvariantCulture)));
            }
            catch { /* 存不下就算了 */ }
        }

        /* ------------------------------------------------ 图标 */

        private static Icon BuildIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    using (var b = new SolidBrush(Color.FromArgb(83, 74, 183)))
                    {
                        g.FillEllipse(b, 1, 1, 30, 30);
                    }
                    using (var p = new Pen(Color.White, 3.2f))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        g.DrawArc(p, 8, 8, 16, 16, -90f, 275f);
                    }
                }
                IntPtr handle = bmp.GetHicon();
                try
                {
                    using (var tmp = Icon.FromHandle(handle))
                    {
                        return (Icon)tmp.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyIcon(IntPtr handle);
    }
}
