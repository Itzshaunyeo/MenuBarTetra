// Build with: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:MenuBarTetraTray.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Program.cs
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MenuBarTetraTray
{
    /// <summary>
    /// Minimal native Windows notification-area host. Double-clicking its icon launches the Unity player
    /// immediately; its context menu also exposes Start Game and Exit without requiring a taskbar window.
    /// </summary>
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var icon = new NotifyIcon())
            using (var menu = new ContextMenuStrip())
            {
                var start = new ToolStripMenuItem("Start game", null, (s, e) => StartGame());
                menu.Items.Add(start);
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Exit", null, (s, e) => Application.Exit());
                icon.Text = "Tetra — click to play";
                icon.Icon = SystemIcons.Application;
                icon.ContextMenuStrip = menu;
                icon.DoubleClick += (s, e) => StartGame();
                icon.Visible = true;
                Application.Run();
                icon.Visible = false;
            }
        }

        static void StartGame()
        {
            var player = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MenuBarTetra.exe");
            if (!File.Exists(player))
            {
                MessageBox.Show("Build the Unity player as MenuBarTetra.exe and place it next to this launcher.", "Tetra", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start(new ProcessStartInfo(player) { WorkingDirectory = Path.GetDirectoryName(player) });
        }
    }
}
