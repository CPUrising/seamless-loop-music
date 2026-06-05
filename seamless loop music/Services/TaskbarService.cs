using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Windows.Interop;
using NAudio.Wave;
using Prism.Events;
using seamless_loop_music.Events;

namespace seamless_loop_music.Services
{
    public class TaskbarService : ITaskbarService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IPlaybackService _playbackService;
        
        private TaskbarItemInfo _taskbarItemInfo;
        private Window _ownerWindow;
        private IntPtr _mainHwnd = IntPtr.Zero;
        private ITaskbarList3 _taskbarList;
        
        private readonly DispatcherTimer _progressTimer;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetIconicThumbnail(IntPtr hwnd, IntPtr hBitmap, int flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmInvalidateIconicBitmaps(IntPtr hwnd);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetIconicLivePreviewBitmap(IntPtr hwnd, IntPtr hBitmap, ref System.Drawing.Point pptClient, uint dwSITFlags);

        private const int WM_DWMSENDICONICTHUMBNAIL = 0x0323;
        private const int WM_DWMSENDICONICLIVEPREVIEWBITMAP = 0x0326;

        public TaskbarService(IEventAggregator eventAggregator, IPlaybackService playbackService)
        {
            _eventAggregator = eventAggregator;
            _playbackService = playbackService;

            _progressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _progressTimer.Tick += ProgressTimer_Tick;

            _eventAggregator.GetEvent<PlaybackStateChangedEvent>().Subscribe(OnPlaybackStateChanged);
            _eventAggregator.GetEvent<TrackLoadedEvent>().Subscribe(OnTrackLoaded);
        }

        public void Initialize(TaskbarItemInfo taskbarItemInfo, Window ownerWindow)
        {
            _taskbarItemInfo = taskbarItemInfo;
            _ownerWindow = ownerWindow;
            
            if (_taskbarItemInfo != null && _taskbarItemInfo.ThumbButtonInfos.Count >= 3)
            {
                _taskbarItemInfo.ThumbButtonInfos[0].Click += (s, e) => _playbackService.Previous();
                _taskbarItemInfo.ThumbButtonInfos[1].Click += (s, e) => {
                    if (_playbackService.PlaybackState == PlaybackState.Playing)
                        _playbackService.Pause();
                    else
                        _playbackService.Play();
                };
                _taskbarItemInfo.ThumbButtonInfos[2].Click += (s, e) => _playbackService.Next();
            }

            if (_ownerWindow != null)
            {
                if (_ownerWindow.IsLoaded)
                {
                    SetupTaskbarDwmIntegration();
                }
                else
                {
                    _ownerWindow.Loaded += (s, e) => SetupTaskbarDwmIntegration();
                }
            }

            UpdateTaskbarState(_playbackService.PlaybackState);
        }

        private void SetupTaskbarDwmIntegration()
        {
            try
            {
                _mainHwnd = new WindowInteropHelper(_ownerWindow).Handle;
                if (_mainHwnd == IntPtr.Zero) return;

                // Initialize ITaskbarList3 COM interface
                var CLSID_TaskbarList = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090");
                var taskbarListType = Type.GetTypeFromCLSID(CLSID_TaskbarList);
                _taskbarList = (ITaskbarList3)Activator.CreateInstance(taskbarListType);
                _taskbarList.HrInit();

                // Enable DWM Iconic representation on the main window directly!
                int forceIconic = 1;
                int hasIconicBitmap = 1;
                DwmSetWindowAttribute(_mainHwnd, 7, ref forceIconic, sizeof(int));   // DWMWA_FORCE_ICONIC_REPRESENTATION
                DwmSetWindowAttribute(_mainHwnd, 10, ref hasIconicBitmap, sizeof(int)); // DWMWA_HAS_ICONIC_BITMAP

                // Add WndProc hook to the main window
                var hwndSource = HwndSource.FromHwnd(_mainHwnd);
                hwndSource?.AddHook(MainWndProc);

                // Initial invalidation to trigger drawing
                DwmInvalidateIconicBitmaps(_mainHwnd);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to setup DWM iconic taskbar integration: {ex.Message}");
            }
        }

        private IntPtr MainWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DWMSENDICONICTHUMBNAIL)
            {
                int maxWidth = (int)(((long)lParam >> 16) & 0xFFFF);
                int maxHeight = (int)((long)lParam & 0xFFFF);

                SendThumbnailToTaskbar(hwnd, maxWidth, maxHeight);
                handled = true;
            }
            else if (msg == WM_DWMSENDICONICLIVEPREVIEWBITMAP)
            {
                SendLivePreviewToTaskbar(hwnd);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void SendThumbnailToTaskbar(IntPtr hwnd, int maxWidth, int maxHeight)
        {
            string coverPath = _playbackService.CurrentTrack?.EffectiveCoverPath;
            System.Drawing.Bitmap bitmap = null;

            try
            {
                // Create a bitmap that matches EXACTLY the dimensions DWM expects.
                // This completely bypasses DWM's aspect ratio scaling bug, eliminating any first-hover stretching!
                bitmap = new System.Drawing.Bitmap(maxWidth, maxHeight);
                
                using (var g = System.Drawing.Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                    // Fill background with a fallback deep dark theme color (#1E1E2E equivalent)
                    using (var bgBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(30, 30, 46)))
                    {
                        g.FillRectangle(bgBrush, 0, 0, maxWidth, maxHeight);
                    }

                    int coverSize = Math.Min(maxWidth, maxHeight);
                    if (coverSize <= 0) coverSize = 120; // Safe fallback

                    // Calculate centered square bounds for the crisp cover art (leaving a slight 6px margin for premium look)
                    int innerMargin = 6;
                    int innerSize = Math.Max(30, coverSize - innerMargin * 2);
                    int x = (maxWidth - innerSize) / 2;
                    int y = (maxHeight - innerSize) / 2;
                    var coverRect = new System.Drawing.Rectangle(x, y, innerSize, innerSize);

                    bool hasLoadedCover = false;

                    if (!string.IsNullOrEmpty(coverPath) && System.IO.File.Exists(coverPath))
                    {
                        try
                        {
                            using (var original = new System.Drawing.Bitmap(coverPath))
                            {
                                // --- GENIUS AMBIENT BLUR BACKGROUND (PROPORTIONAL CROP) ---
                                // 1. Create a tiny bitmap matching the aspect ratio of the taskbar thumbnail preview (e.g. 24x16)
                                int tinyW = 24;
                                int tinyH = 16;
                                using (var tinyBmp = new System.Drawing.Bitmap(tinyW, tinyH))
                                {
                                    using (var gTiny = System.Drawing.Graphics.FromImage(tinyBmp))
                                    {
                                        gTiny.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                                        gTiny.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                        gTiny.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                                        // Apply UniformToFill to scale the square cover to fill the tiny rectangular canvas proportionally
                                        double tinyScaleX = (double)tinyW / original.Width;
                                        double tinyScaleY = (double)tinyH / original.Height;
                                        double tinyScale = Math.Max(tinyScaleX, tinyScaleY);

                                        int tinyDrawW = (int)Math.Round(original.Width * tinyScale);
                                        int tinyDrawH = (int)Math.Round(original.Height * tinyScale);
                                        int tinyX = (tinyW - tinyDrawW) / 2;
                                        int tinyY = (tinyH - tinyDrawH) / 2;

                                        gTiny.DrawImage(original, new System.Drawing.Rectangle(tinyX, tinyY, tinyDrawW, tinyDrawH));
                                    }

                                    // 2. Scale the tiny cropped image back up to full size to create a perfect, unstretched blur!
                                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                    g.DrawImage(tinyBmp, new System.Drawing.Rectangle(0, 0, maxWidth, maxHeight));
                                }

                                // 3. Draw a semi-transparent dark overlay over the blurred background to darken it and add depth
                                using (var darkenBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(170, 20, 20, 20)))
                                {
                                    g.FillRectangle(darkenBrush, 0, 0, maxWidth, maxHeight);
                                }

                                // --- DRAW CRISP CENTERED COVER ---
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.DrawImage(original, coverRect);

                                // 4. Draw a subtle thin light border around the centered square for a polished look
                                using (var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(60, 255, 255, 255), 1))
                                {
                                    g.DrawRectangle(borderPen, coverRect);
                                }

                                hasLoadedCover = true;
                            }
                        }
                        catch
                        {
                        }
                    }

                    if (!hasLoadedCover)
                    {
                        // Fallback: draw beautiful note icon centered in the square
                        using (var musicNoteBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                            new System.Drawing.Point(coverRect.Left, coverRect.Top),
                            new System.Drawing.Point(coverRect.Left, coverRect.Bottom),
                            System.Drawing.Color.FromArgb(48, 48, 62),
                            System.Drawing.Color.FromArgb(28, 28, 36)))
                        {
                            g.FillRectangle(musicNoteBrush, coverRect);
                        }

                        using (var fontNote = new System.Drawing.Font("Segoe UI Symbol", (float)(innerSize * 0.4), System.Drawing.FontStyle.Regular))
                        using (var brushNote = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(120, 140, 160, 200)))
                        {
                            var sf = new System.Drawing.StringFormat
                            {
                                Alignment = System.Drawing.StringAlignment.Center,
                                LineAlignment = System.Drawing.StringAlignment.Center
                            };
                            g.DrawString("♫", fontNote, brushNote, coverRect, sf);
                        }
                    }
                }

                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    // Submit the fully-dimensioned bitmap to DWM (no scaling or stretching will occur!)
                    DwmSetIconicThumbnail(hwnd, hBitmap, 0);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending iconic thumbnail: {ex.Message}");
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        private void OnTrackLoaded(Models.MusicTrack track)
        {
            UpdateTaskbarState(_playbackService.PlaybackState);
            InvalidateThumbnail();
        }

        private void OnPlaybackStateChanged(PlaybackState state)
        {
            UpdateTaskbarState(state);
            InvalidateThumbnail();
        }

        private void InvalidateThumbnail()
        {
            if (_mainHwnd != IntPtr.Zero)
            {
                try
                {
                    DwmInvalidateIconicBitmaps(_mainHwnd);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to invalidate iconic bitmaps: {ex.Message}");
                }
            }
        }

        private void UpdateTaskbarState(PlaybackState state)
        {
            if (_taskbarItemInfo == null) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // Toggle native Play/Pause icons
                if (_taskbarItemInfo.ThumbButtonInfos.Count >= 2)
                {
                    var playPauseBtn = _taskbarItemInfo.ThumbButtonInfos[1];
                    if (state == PlaybackState.Playing)
                    {
                        playPauseBtn.ImageSource = (System.Windows.Media.ImageSource)System.Windows.Application.Current.FindResource("DrawingIconPause");
                        playPauseBtn.Description = "Pause";
                    }
                    else
                    {
                        playPauseBtn.ImageSource = (System.Windows.Media.ImageSource)System.Windows.Application.Current.FindResource("DrawingIconPlay");
                        playPauseBtn.Description = "Play";
                    }
                }

                switch (state)
                {
                    case PlaybackState.Playing:
                        _taskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
                        if (!_progressTimer.IsEnabled) _progressTimer.Start();
                        break;
                    case PlaybackState.Paused:
                        _taskbarItemInfo.ProgressState = TaskbarItemProgressState.Paused;
                        if (_progressTimer.IsEnabled) _progressTimer.Stop();
                        UpdateProgress();
                        break;
                    case PlaybackState.Stopped:
                    default:
                        _taskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
                        if (_progressTimer.IsEnabled) _progressTimer.Stop();
                        _taskbarItemInfo.ProgressValue = 0;
                        break;
                }
            });
        }

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            UpdateProgress();
        }

        private void UpdateProgress()
        {
            if (_taskbarItemInfo == null) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var total = _playbackService.TotalTime;
                if (total.TotalSeconds > 0)
                {
                    var progress = _playbackService.CurrentTime.TotalSeconds / total.TotalSeconds;
                    _taskbarItemInfo.ProgressValue = Math.Max(0, Math.Min(1, progress));
                }
                else
                {
                    _taskbarItemInfo.ProgressValue = 0;
                }
            });
        }

        private void SendLivePreviewToTaskbar(IntPtr hwnd)
        {
            if (_ownerWindow == null) return;

            try
            {
                _ownerWindow.Dispatcher.Invoke(() =>
                {
                    double width = _ownerWindow.ActualWidth;
                    double height = _ownerWindow.ActualHeight;

                    if (_ownerWindow.WindowState == WindowState.Minimized)
                    {
                        var bounds = _ownerWindow.RestoreBounds;
                        if (bounds.Width > 0 && bounds.Height > 0)
                        {
                            width = bounds.Width;
                            height = bounds.Height;
                        }
                    }

                    if (double.IsNaN(width) || width <= 0) width = 1024;
                    if (double.IsNaN(height) || height <= 0) height = 700;

                    // Get system DPI scaling for this window to prevent shrink effect on high DPI screens
                    var dpiScale = System.Windows.Media.VisualTreeHelper.GetDpi(_ownerWindow);
                    double dpiX = dpiScale.PixelsPerInchX;
                    double dpiY = dpiScale.PixelsPerInchY;

                    int bmpW = (int)Math.Round(width * dpiScale.DpiScaleX);
                    int bmpH = (int)Math.Round(height * dpiScale.DpiScaleY);

                    var renderTarget = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        bmpW, bmpH, dpiX, dpiY, System.Windows.Media.PixelFormats.Pbgra32);
                    
                    renderTarget.Render(_ownerWindow);

                    using (var bitmap = BitmapSourceToBitmap(renderTarget))
                    {
                        IntPtr hBitmap = bitmap.GetHbitmap();
                        try
                        {
                            var point = new System.Drawing.Point(0, 0);
                            DwmSetIconicLivePreviewBitmap(hwnd, hBitmap, ref point, 0);
                        }
                        finally
                        {
                            DeleteObject(hBitmap);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending iconic live preview bitmap: {ex.Message}");
            }
        }

        private System.Drawing.Bitmap BitmapSourceToBitmap(System.Windows.Media.Imaging.BitmapSource source)
        {
            var bmp = new System.Drawing.Bitmap(source.PixelWidth, source.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var data = bmp.LockBits(
                new System.Drawing.Rectangle(System.Drawing.Point.Empty, bmp.Size),
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            
            try
            {
                source.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Height * data.Stride, data.Stride);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            
            return bmp;
        }
    }
}
