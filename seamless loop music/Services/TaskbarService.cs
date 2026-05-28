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
        private readonly DispatcherTimer _progressTimer;

        private Window _proxyWindow;
        private IntPtr _proxyHwnd = IntPtr.Zero;
        private IntPtr _mainHwnd = IntPtr.Zero;
        private ITaskbarList3 _taskbarList;
        private bool _buttonsAdded = false;

        private static readonly Guid CLSID_TaskbarList = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090");

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetIconicThumbnail(IntPtr hwnd, IntPtr hBitmap, int flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmInvalidateIconicBitmaps(IntPtr hwnd);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint ID_PREV = 101;
        private const uint ID_PLAYPAUSE = 102;
        private const uint ID_NEXT = 103;

        private IntPtr _hIconPrev = IntPtr.Zero;
        private IntPtr _hIconPlayPause = IntPtr.Zero;
        private IntPtr _hIconNext = IntPtr.Zero;

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

            if (_taskbarItemInfo != null)
            {
                _taskbarItemInfo.ThumbButtonInfos.Clear();
                _taskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
            }

            if (ownerWindow != null)
            {
                if (ownerWindow.IsLoaded)
                {
                    SetupProxyWindow(ownerWindow);
                }
                else
                {
                    ownerWindow.Loaded += (s, e) => SetupProxyWindow(ownerWindow);
                }
            }
        }
        private void SetupProxyWindow(Window ownerWindow)
        {
            try
            {
                System.IO.File.WriteAllText("proxy_debug.txt", $"[{DateTime.Now}] SetupProxyWindow starting.\n");
                _mainHwnd = new WindowInteropHelper(ownerWindow).Handle;
                if (_mainHwnd == IntPtr.Zero) return;
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] _mainHwnd={_mainHwnd}\n");

                _proxyWindow = new Window
                {
                    Title = "Seamless Loop Proxy",
                    Width = 120,
                    Height = 150,
                    WindowStyle = WindowStyle.SingleBorderWindow, // 使用标准窗口样式以符合 DWM 规范
                    ShowInTaskbar = false,
                    Left = -10000,
                    Top = -10000,
                    Visibility = Visibility.Hidden // 隐藏窗口，由 RegisterTab 接管自绘
                };

                _proxyHwnd = new WindowInteropHelper(_proxyWindow).EnsureHandle();
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] EnsureHandle completed. _proxyHwnd={_proxyHwnd}\n");
                InitializeTaskbarIntegration();
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] SetupProxyWindow EXCEPTION: {ex}\n");
            }
        }

        private void InitializeTaskbarIntegration()
        {
            try
            {
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] InitializeTaskbarIntegration starting. _proxyHwnd={_proxyHwnd}, _mainHwnd={_mainHwnd}\n");
                if (_proxyHwnd == IntPtr.Zero || _mainHwnd == IntPtr.Zero) return;

                var taskbarListType = Type.GetTypeFromCLSID(CLSID_TaskbarList);
                _taskbarList = (ITaskbarList3)Activator.CreateInstance(taskbarListType);
                _taskbarList.HrInit();
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] ITaskbarList3 created and initialized successfully.\n");

                // 在代理窗口 _proxyHwnd 上启用 Iconic 属性！
                int forceIconic = 1;
                int hasIconicBitmap = 1;
                int res1 = DwmSetWindowAttribute(_proxyHwnd, 7, ref forceIconic, sizeof(int));
                int res2 = DwmSetWindowAttribute(_proxyHwnd, 10, ref hasIconicBitmap, sizeof(int));
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] _proxyHwnd DwmSetWindowAttribute completed. res1={res1}, res2={res2}\n");

                _taskbarList.RegisterTab(_proxyHwnd, _mainHwnd);
                _taskbarList.SetTabOrder(_proxyHwnd, _mainHwnd);
                _taskbarList.SetTabActive(_proxyHwnd, _mainHwnd, 0);
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] RegisterTab, SetTabOrder and SetTabActive completed.\n");

                // 把 Hook 挂载在代理窗口 _proxyHwnd 上！
                var hwndSource = HwndSource.FromHwnd(_proxyHwnd);
                hwndSource?.AddHook(ProxyWndProc);
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] _proxyHwnd HwndSource Hook added.\n");

                // 发送首个重绘消息给代理窗口 _proxyHwnd
                SendMessage(_proxyHwnd, 0x0323, IntPtr.Zero, (IntPtr)((120 << 16) | 150));
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] SendMessage WM_DWMSENDICONICTHUMBNAIL sent to _proxyHwnd.\n");

                UpdateTaskbarState(_playbackService.PlaybackState);
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] InitializeTaskbarIntegration EXCEPTION: {ex}\n");
            }
        }

        private void AddThumbButtons()
        {
            try
            {
                if (_taskbarList == null || _proxyHwnd == IntPtr.Zero || _buttonsAdded) return;

                ReleaseButtonIcons();

                _hIconPrev = CreateArrowIcon(true);
                _hIconPlayPause = CreatePlayPauseIcon(_playbackService.PlaybackState == PlaybackState.Playing);
                _hIconNext = CreateArrowIcon(false);

                var buttons = new THUMBBUTTON[3];

                // 使用 THB_ICON | THB_TOOLTIP (0x2 | 0x4 = 6)，排除错误的 THB_BITMAP (0x1) 干扰
                buttons[0].dwMask = 0x2 | 0x4;
                buttons[0].iId = ID_PREV;
                buttons[0].hIcon = _hIconPrev;
                buttons[0].szTip = "Previous Track";
                buttons[0].dwFlags = 0x0;

                buttons[1].dwMask = 0x2 | 0x4;
                buttons[1].iId = ID_PLAYPAUSE;
                buttons[1].hIcon = _hIconPlayPause;
                buttons[1].szTip = _playbackService.PlaybackState == PlaybackState.Playing ? "Pause" : "Play";
                buttons[1].dwFlags = 0x0;

                buttons[2].dwMask = 0x2 | 0x4;
                buttons[2].iId = ID_NEXT;
                buttons[2].hIcon = _hIconNext;
                buttons[2].szTip = "Next Track";
                buttons[2].dwFlags = 0x0;

                _taskbarList.ThumbBarAddButtons(_proxyHwnd, 3, buttons);
                _buttonsAdded = true;
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] ThumbBarAddButtons on _proxyHwnd completed successfully.\n");
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] AddThumbButtons EXCEPTION: {ex}\n");
            }
        }

        private void UpdateThumbButtons()
        {
            try
            {
                if (_taskbarList == null || _proxyHwnd == IntPtr.Zero || !_buttonsAdded) return;

                if (_hIconPlayPause != IntPtr.Zero)
                {
                    DestroyIcon(_hIconPlayPause);
                }
                _hIconPlayPause = CreatePlayPauseIcon(_playbackService.PlaybackState == PlaybackState.Playing);

                var buttons = new THUMBBUTTON[3];

                // 使用 THB_ICON | THB_TOOLTIP (0x2 | 0x4 = 6) 确保更新正确的状态
                buttons[0].dwMask = 0x2 | 0x4;
                buttons[0].iId = ID_PREV;
                buttons[0].hIcon = _hIconPrev;
                buttons[0].szTip = "Previous Track";

                buttons[1].dwMask = 0x2 | 0x4;
                buttons[1].iId = ID_PLAYPAUSE;
                buttons[1].hIcon = _hIconPlayPause;
                buttons[1].szTip = _playbackService.PlaybackState == PlaybackState.Playing ? "Pause" : "Play";

                buttons[2].dwMask = 0x2 | 0x4;
                buttons[2].iId = ID_NEXT;
                buttons[2].hIcon = _hIconNext;
                buttons[2].szTip = "Next Track";

                _taskbarList.ThumbBarUpdateButtons(_proxyHwnd, 3, buttons);
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] ThumbBarUpdateButtons on _proxyHwnd completed successfully.\n");
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] UpdateThumbButtons EXCEPTION: {ex}\n");
            }
        }

        private IntPtr ProxyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0323) // WM_DWMSENDICONICTHUMBNAIL
            {
                int width = (int)(((long)lParam >> 16) & 0xFFFF);
                int height = (int)((long)lParam & 0xFFFF);

                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] ProxyWndProc received WM_DWMSENDICONICTHUMBNAIL. width={width}, height={height}\n");

                if (!_buttonsAdded)
                {
                    AddThumbButtons();
                }

                SendThumbnailToTaskbar(hwnd); // 固定宽高自绘给 _proxyHwnd
                handled = true;
            }
            else if (msg == 0x0111) // WM_COMMAND
            {
                uint buttonId = (uint)LOWORD(wParam);
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] ProxyWndProc received WM_COMMAND. buttonId={buttonId}\n");
                if (buttonId == ID_PREV)
                {
                    _playbackService.Previous();
                    handled = true;
                }
                else if (buttonId == ID_PLAYPAUSE)
                {
                    if (_playbackService.PlaybackState == PlaybackState.Playing)
                        _playbackService.Pause();
                    else
                        _playbackService.Play();
                    handled = true;
                }
                else if (buttonId == ID_NEXT)
                {
                    _playbackService.Next();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void SendThumbnailToTaskbar(IntPtr hwnd)
        {
            string coverPath = _playbackService.CurrentTrack?.EffectiveCoverPath;
            System.Drawing.Bitmap bitmap = null;

            // 固定尺寸绘制，逼迫 DWM 在缩放时展示黄金竖屏卡片比例 120 x 150
            int width = 120;
            int height = 150;

            try
            {
                bitmap = new System.Drawing.Bitmap(width, height);
                using (var g = System.Drawing.Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                    // 高逼格深色渐变背景
                    using (var cardBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new System.Drawing.Point(0, 0),
                        new System.Drawing.Point(0, height),
                        System.Drawing.Color.FromArgb(32, 32, 42),
                        System.Drawing.Color.FromArgb(16, 16, 22)))
                    {
                        g.FillRectangle(cardBrush, 0, 0, width, height);
                    }

                    // 正方形专辑封面 120x120，完美展现
                    int coverSize = 120;
                    var coverRect = new System.Drawing.Rectangle(0, 0, coverSize, coverSize);

                    bool hasLoadedCover = false;
                    if (!string.IsNullOrEmpty(coverPath) && System.IO.File.Exists(coverPath))
                    {
                        try
                        {
                            using (var original = new System.Drawing.Bitmap(coverPath))
                            {
                                g.DrawImage(original, coverRect);
                                hasLoadedCover = true;
                            }
                        }
                        catch
                        {
                        }
                    }

                    if (!hasLoadedCover)
                    {
                        using (var musicNoteBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                            new System.Drawing.Point(coverRect.Left, coverRect.Top),
                            new System.Drawing.Point(coverRect.Left, coverRect.Bottom),
                            System.Drawing.Color.FromArgb(48, 48, 62),
                            System.Drawing.Color.FromArgb(28, 28, 36)))
                        {
                            g.FillRectangle(musicNoteBrush, coverRect);
                        }

                        using (var fontNote = new System.Drawing.Font("Segoe UI Symbol", 28f, System.Drawing.FontStyle.Regular))
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

                    int infoHeight = height - coverSize; // 30 像素
                    string artist = _playbackService.CurrentTrack?.Artist ?? "Unknown Artist";
                    string trackTitle = _playbackService.CurrentTrack?.Title ?? "No Track Playing";

                    using (var fontTitle = new System.Drawing.Font("Segoe UI", 7.2f, System.Drawing.FontStyle.Bold))
                    using (var fontArtist = new System.Drawing.Font("Segoe UI", 6.2f, System.Drawing.FontStyle.Regular))
                    using (var brushText = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(240, 240, 240)))
                    using (var brushSubText = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(160, 170, 185)))
                    {
                        var sf = new System.Drawing.StringFormat
                        {
                            Alignment = System.Drawing.StringAlignment.Center,
                            LineAlignment = System.Drawing.StringAlignment.Center,
                            Trimming = System.Drawing.StringTrimming.EllipsisCharacter,
                            FormatFlags = System.Drawing.StringFormatFlags.NoWrap
                        };

                        float textPadding = 1f;
                        float titleHeight = infoHeight * 0.52f;
                        float artistHeight = infoHeight * 0.44f;

                        var titleRect = new System.Drawing.RectangleF(4, coverSize + textPadding, width - 8, titleHeight);
                        var artistRect = new System.Drawing.RectangleF(4, coverSize + titleHeight, width - 8, artistHeight);

                        g.DrawString(trackTitle, fontTitle, brushText, titleRect, sf);
                        g.DrawString(artist, fontArtist, brushSubText, artistRect, sf);
                    }

                    // 底部微光蓝色横线条
                    using (var lineBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new System.Drawing.Point(0, 0),
                        new System.Drawing.Point(width, 0),
                        System.Drawing.Color.FromArgb(80, 122, 180, 255),
                        System.Drawing.Color.FromArgb(20, 122, 180, 255)))
                    {
                        using (var pen = new System.Drawing.Pen(lineBrush, 1.5f))
                        {
                            g.DrawLine(pen, 0, height - 1, width, height - 1);
                        }
                    }
                }

                if (bitmap != null)
                {
                    IntPtr hBitmap = bitmap.GetHbitmap();
                    try
                    {
                        DwmSetIconicThumbnail(hwnd, hBitmap, 0);
                        System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] DwmSetIconicThumbnail called on hwnd={hwnd} successfully.\n");
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] SendThumbnailToTaskbar EXCEPTION: {ex}\n");
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        private void OnTrackLoaded(Models.MusicTrack track)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateTaskbarState(_playbackService.PlaybackState);
                if (_proxyHwnd != IntPtr.Zero && _mainHwnd != IntPtr.Zero)
                {
                    try
                    {
                        _taskbarList.SetTabActive(_proxyHwnd, _mainHwnd, 0);
                        DwmInvalidateIconicBitmaps(_proxyHwnd);
                    }
                    catch (Exception ex)
                    {
                        System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] OnTrackLoaded SetTabActive/Invalidate EXCEPTION: {ex}\n");
                    }
                }
            });
        }

        private void OnPlaybackStateChanged(PlaybackState state)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateTaskbarState(state);
                UpdateThumbButtons();
                if (_proxyHwnd != IntPtr.Zero && _mainHwnd != IntPtr.Zero)
                {
                    try
                    {
                        _taskbarList.SetTabActive(_proxyHwnd, _mainHwnd, 0);
                        DwmInvalidateIconicBitmaps(_proxyHwnd);
                    }
                    catch (Exception ex)
                    {
                        System.IO.File.AppendAllText("proxy_debug.txt", $"[{DateTime.Now}] OnPlaybackStateChanged SetTabActive/Invalidate EXCEPTION: {ex}\n");
                    }
                }
            });
        }

        private void UpdateTaskbarState(PlaybackState state)
        {
            if (_taskbarList == null || _mainHwnd == IntPtr.Zero) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                switch (state)
                {
                    case PlaybackState.Playing:
                        _taskbarList.SetProgressState(_mainHwnd, 0x01);
                        if (!_progressTimer.IsEnabled) _progressTimer.Start();
                        break;
                    case PlaybackState.Paused:
                        _taskbarList.SetProgressState(_mainHwnd, 0x02);
                        if (_progressTimer.IsEnabled) _progressTimer.Stop();
                        UpdateProgress();
                        break;
                    case PlaybackState.Stopped:
                    default:
                        _taskbarList.SetProgressState(_mainHwnd, 0x00);
                        if (_progressTimer.IsEnabled) _progressTimer.Stop();
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
            if (_taskbarList == null || _mainHwnd == IntPtr.Zero) return;

            var total = _playbackService.TotalTime;
            if (total.TotalSeconds > 0)
            {
                var progress = _playbackService.CurrentTime.TotalSeconds / total.TotalSeconds;
                ulong current = (ulong)(progress * 1000);
                _taskbarList.SetProgressValue(_mainHwnd, current, 1000);
            }
            else
            {
                _taskbarList.SetProgressValue(_mainHwnd, 0, 1000);
            }
        }

        // Hicon 生成辅助方法
        private IntPtr CreateArrowIcon(bool isLeft)
        {
            using (var bmp = new System.Drawing.Bitmap(32, 32))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(System.Drawing.Color.Transparent);

                    using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(220, 220, 220)))
                    {
                        var path = new System.Drawing.Drawing2D.GraphicsPath();
                        if (isLeft)
                        {
                            // 左箭头
                            path.AddPolygon(new[] {
                                new System.Drawing.PointF(14, 16),
                                new System.Drawing.PointF(24, 7),
                                new System.Drawing.PointF(24, 25)
                            });
                            path.AddPolygon(new[] {
                                new System.Drawing.PointF(4, 16),
                                new System.Drawing.PointF(14, 7),
                                new System.Drawing.PointF(14, 25)
                            });
                        }
                        else
                        {
                            // 右箭头
                            path.AddPolygon(new[] {
                                new System.Drawing.PointF(18, 16),
                                new System.Drawing.PointF(8, 7),
                                new System.Drawing.PointF(8, 25)
                            });
                            path.AddPolygon(new[] {
                                new System.Drawing.PointF(28, 16),
                                new System.Drawing.PointF(18, 7),
                                new System.Drawing.PointF(18, 25)
                            });
                        }
                        g.FillPath(brush, path);
                    }
                }
                return bmp.GetHicon();
            }
        }

        private IntPtr CreatePlayPauseIcon(bool isPlaying)
        {
            using (var bmp = new System.Drawing.Bitmap(32, 32))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(System.Drawing.Color.Transparent);

                    using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(220, 220, 220)))
                    {
                        if (isPlaying)
                        {
                            // 暂停符 ‖
                            g.FillRectangle(brush, 8, 6, 5, 20);
                            g.FillRectangle(brush, 19, 6, 5, 20);
                        }
                        else
                        {
                            // 播放符 ▶
                            var path = new System.Drawing.Drawing2D.GraphicsPath();
                            path.AddPolygon(new[] {
                                new System.Drawing.PointF(8, 6),
                                new System.Drawing.PointF(26, 16),
                                new System.Drawing.PointF(8, 26)
                            });
                            g.FillPath(brush, path);
                        }
                    }
                }
                return bmp.GetHicon();
            }
        }

        private void ReleaseButtonIcons()
        {
            if (_hIconPrev != IntPtr.Zero) { DestroyIcon(_hIconPrev); _hIconPrev = IntPtr.Zero; }
            if (_hIconPlayPause != IntPtr.Zero) { DestroyIcon(_hIconPlayPause); _hIconPlayPause = IntPtr.Zero; }
            if (_hIconNext != IntPtr.Zero) { DestroyIcon(_hIconNext); _hIconNext = IntPtr.Zero; }
        }

        private static int LOWORD(IntPtr value)
        {
            return (int)((long)value & 0xFFFF);
        }

        ~TaskbarService()
        {
            ReleaseButtonIcons();
        }
    }
}
