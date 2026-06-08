using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;
using seamless_loop_music.UI.Controls;
using System.Windows.Threading;
using System.Diagnostics; // 确保引入了 Engine 所在的命名空间

namespace seamless_loop_music
{
    public partial class style : ResourceDictionary
    {
        public style()
        {
            InitializeComponent();
        }

        /*about Marquee behavior*/

        private static DispatcherTimer _debounceTimer;
        // 缓存当前正在互动的控件
        private static Canvas _activeCanvas;
        private static ContentPresenter _activePresenter;
        private const double SCROLL_SPEED_PER_SECOND = 40.0;
        private const double STAY_DURATION_SECONDS = 2.0;

        private void CheckAndAnimate(Canvas canvas, ContentPresenter presenter)
        {
            if (canvas == null || presenter == null) return;

            // 1. 初始化防抖计时器（只需要创建一次）
            if (_debounceTimer == null)
            {
                _debounceTimer = new DispatcherTimer(DispatcherPriority.Background);
                // 100毫秒内只要没有新拉伸，就判定用户松手了
                _debounceTimer.Interval = TimeSpan.FromMilliseconds(300);
                _debounceTimer.Tick += DebounceTimer_Tick;
            }

            // 2. 将当前正在互动的控件暂存
            _activeCanvas = canvas;
            _activePresenter = presenter;

            // 3. 强行切断当前动画，将文字定在原地，防止在拖拽中计算
            ResetToStatic(presenter);

            // 4. 重置计时器：只要用户还在拖，就一直重启计时器，阻止动画代码的执行
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private static void DebounceTimer_Tick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            if (_activeCanvas == null || _activePresenter == null) return;
            _activeCanvas.Dispatcher.BeginInvoke(new Action(() =>
            {
               // _activeCanvas.UpdateLayout();
               // _activePresenter.UpdateLayout();

                double windowWidth = _activeCanvas.ActualWidth;
            double textWidth = _activePresenter.ActualWidth;
            // 输出调试信息
            if (textWidth<= 0 || windowWidth <= 0)
            {
                Debug.WriteLine($"[Marquee] 无效尺寸 - 文本宽度: {textWidth}, 窗口宽度: {windowWidth}");
                return;
            }
                

            PropertyPath xPropertyPath = new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)");
            TranslateTransform transform = _activePresenter.RenderTransform as TranslateTransform;
            if (transform == null) return;

            // 2. 判定文本是否真正超出边界
            if (textWidth > windowWidth && windowWidth > 0)
            {
                // 计算位移
                double scrollDistance =textWidth - windowWidth;
                // 计算时间
                double moveSeconds = scrollDistance / SCROLL_SPEED_PER_SECOND;

                // 使用关键帧动画
                DoubleAnimationUsingKeyFrames animation = new DoubleAnimationUsingKeyFrames
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };

                // 节点 1：起点静止期（0s ➔ 2s，物理坐标保持在 0）
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(
                    0,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(STAY_DURATION_SECONDS))));

                // 节点 2：匀速滑动期（2s ➔ 2s+动耗，滑到末尾负坐标）
                double timeToEnd = STAY_DURATION_SECONDS + moveSeconds;
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(
                    -scrollDistance,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(timeToEnd))));

                // 节点 3：末尾静止期（末尾 ➔ 末尾+2s，保持在负坐标静止，供用户阅读末尾字）
                double timeEndStay = timeToEnd + STAY_DURATION_SECONDS;
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(
                    -scrollDistance,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(timeEndStay))));

                // 节点 4：匀速回滚期（末尾+2s ➔ 完整周期，完美匀速撤回起点 0）
                double totalLoopTime = timeEndStay + moveSeconds;
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(
                    0,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(totalLoopTime))));

                Storyboard.SetTarget(animation, _activePresenter);
                Storyboard.SetTargetProperty(animation, xPropertyPath);
                Storyboard storyboard = new Storyboard();
                storyboard.Children.Add(animation);
                storyboard.Begin();
            }
            else // 空间足够时，强行掐断动画，文字回归原位
            {
                DoubleAnimation resetAnimation = new DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.Zero // 0秒瞬间归位
                };

                Storyboard.SetTarget(resetAnimation, _activePresenter);
                Storyboard.SetTargetProperty(resetAnimation, xPropertyPath);
                Storyboard storyboard = new Storyboard();
                storyboard.Children.Add(resetAnimation);
                storyboard.Begin();
            }}), DispatcherPriority.Background);
        }
        /// <summary>
        /// 轻量级强控重置：将文字归位
        /// </summary>
        private static void ResetToStatic(ContentPresenter presenter)
        {
            PropertyPath xPropertyPath = new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)");
            DoubleAnimation resetAnimation = new DoubleAnimation { To = 0, Duration = TimeSpan.Zero };
            Storyboard.SetTarget(resetAnimation, presenter);
            Storyboard.SetTargetProperty(resetAnimation, xPropertyPath);

            Storyboard storyboard = new Storyboard();
            storyboard.Children.Add(resetAnimation);
            storyboard.Begin();
        }
        private void Marquee_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Canvas canvas && canvas.Children.Count > 0 && canvas.Children[0] is ContentPresenter presenter)
            {
                // 一键调用恒定速率引擎
                CheckAndAnimate(canvas, presenter);
            }
        }
        // 当任何一个应用了该样式的组件“更换了文本内容”（比如换了歌、换了路径）
        private void Marquee_TextUpdated(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is ContentPresenter presenter && presenter.Parent is Canvas canvas)
            {
                // 一键调用恒定速率引擎
                CheckAndAnimate(canvas, presenter);
            }
        }
        private void Marquee_TargetUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
        {
            if (sender is ContentPresenter presenter && presenter.Parent is Canvas canvas)
            {
                // 一键调用恒定速率引擎
                CheckAndAnimate(canvas, presenter);
            }
        }

        // 某组件被加载时
        private void Marquee_Loaded(object sender, RoutedEventArgs e)
        {
             if (sender is ContentPresenter presenter && presenter.Parent is Canvas canvas)
                    CheckAndAnimate(canvas, presenter);
        }
    }
}