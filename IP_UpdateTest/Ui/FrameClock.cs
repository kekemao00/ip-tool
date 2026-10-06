using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 全局重绘时钟：只在有东西在动时运行（约 120Hz），只负责请求重绘；
    /// 每个控件在绘制时用 Motion.Now 自己算样式，还在动就再登记一次
    /// </summary>
    public static class FrameClock
    {
        private const int IntervalMilliseconds = 8;

        private static readonly HashSet<Control> Pending = new HashSet<Control>();
        private static readonly List<Func<bool>> Tickers = new List<Func<bool>>();
        private static Timer timer;
        private static bool highResolution;

        /// <summary>
        /// 下一帧重绘这个控件
        /// </summary>
        public static void Request(Control control)
        {
            if (control == null || control.IsDisposed || Motion.IsFrozen) return;
            Pending.Add(control);
            Start();
        }

        /// <summary>
        /// 每帧调用 tick，返回 false 时移除（用于窗体透明度等不靠重绘的动画）
        /// </summary>
        public static void Run(Func<bool> tick)
        {
            if (Motion.IsFrozen) return;
            Tickers.Add(tick);
            Start();
        }

        private static void Start()
        {
            if (timer == null)
            {
                timer = new Timer { Interval = IntervalMilliseconds };
                timer.Tick += OnTick;
            }
            if (timer.Enabled) return;
            // 默认计时精度约 15.6ms，动画期间临时提高到 1ms
            if (!highResolution) highResolution = timeBeginPeriod(1) == 0;
            timer.Start();
        }

        private static void OnTick(object sender, EventArgs e)
        {
            if (Pending.Count == 0 && Tickers.Count == 0)
            {
                timer.Stop();
                if (highResolution)
                {
                    timeEndPeriod(1);
                    highResolution = false;
                }
                return;
            }

            var controls = new List<Control>(Pending);
            Pending.Clear();
            foreach (Control control in controls)
            {
                if (!control.IsDisposed && control.IsHandleCreated) control.Invalidate();
            }

            for (int i = Tickers.Count - 1; i >= 0; i--)
            {
                bool keep;
                try
                {
                    keep = Tickers[i]();
                }
                catch (ObjectDisposedException)
                {
                    keep = false;
                }
                if (!keep) Tickers.RemoveAt(i);
            }
        }

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint period);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint period);
    }
}
