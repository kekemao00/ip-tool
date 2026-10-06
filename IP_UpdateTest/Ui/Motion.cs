using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 弹簧参数：response 为无阻尼周期（秒），damping 为阻尼比。用解析解计算，不逐帧积分
    /// </summary>
    public struct Spring
    {
        /// <summary>悬停、按下、焦点、透明度</summary>
        public static readonly Spring Snappy = new Spring(0.26, 0.86);

        /// <summary>位置、尺寸、高亮块滑动</summary>
        public static readonly Spring Default = new Spring(0.38, 0.82);

        /// <summary>面板展开、通知胶囊、页面入场</summary>
        public static readonly Spring Smooth = new Spring(0.50, 0.86);

        /// <summary>指示条前沿</summary>
        public static readonly Spring Lead = new Spring(0.26, 0.84);

        /// <summary>指示条后沿</summary>
        public static readonly Spring Trail = new Spring(0.46, 0.86);

        /// <summary>只用于错误抖动</summary>
        public static readonly Spring Shake = new Spring(0.16, 0.32);

        /// <summary>
        /// 剩余幅度低于初始幅度的这个比例时视为静止
        /// </summary>
        private const double RestThreshold = 1e-4;

        public Spring(double response, double damping)
        {
            Response = response;
            Damping = damping;
            Omega = 2 * Math.PI / response;
            DampedOmega = Omega * Math.Sqrt(1 - damping * damping);
            Decay = damping * Omega;
            // 包络 e^(-ζωt)·(1 + ζω/ωd) 降到阈值以下的时间
            SettleTime = Math.Log((1 + Decay / DampedOmega) / RestThreshold) / Decay;
        }

        public double Response { get; private set; }

        public double Damping { get; private set; }

        /// <summary>无阻尼角频率 ω</summary>
        public double Omega { get; private set; }

        /// <summary>有阻尼角频率 ωd</summary>
        public double DampedOmega { get; private set; }

        /// <summary>衰减率 ζω</summary>
        public double Decay { get; private set; }

        /// <summary>从开始到视为静止的时间（秒）</summary>
        public double SettleTime { get; private set; }

        /// <summary>质量为 1 时的刚度</summary>
        public double Stiffness
        {
            get { return Omega * Omega; }
        }

        /// <summary>质量为 1 时的阻尼系数</summary>
        public double DampingCoefficient
        {
            get { return 2 * Decay; }
        }

        /// <summary>
        /// 0 → 1 的单位阶跃响应
        /// </summary>
        public double Step(double t)
        {
            if (t <= 0) return 0;
            double e = Math.Exp(-Decay * t);
            return 1 - e * (Math.Cos(DampedOmega * t) + Decay / DampedOmega * Math.Sin(DampedOmega * t));
        }

        /// <summary>
        /// 单位阶跃响应的速度
        /// </summary>
        public double StepVelocity(double t)
        {
            if (t <= 0) return 0;
            return Math.Exp(-Decay * t) * Omega * Omega / DampedOmega * Math.Sin(DampedOmega * t);
        }

        /// <summary>
        /// 初速度为 1、终点为 0 的响应（用来接住松手时的速度）
        /// </summary>
        public double Impulse(double t)
        {
            if (t <= 0) return 0;
            return Math.Exp(-Decay * t) * Math.Sin(DampedOmega * t) / DampedOmega;
        }

        public double ImpulseVelocity(double t)
        {
            if (t < 0) return 0;
            double e = Math.Exp(-Decay * t);
            return e * (Math.Cos(DampedOmega * t) - Decay / DampedOmega * Math.Sin(DampedOmega * t));
        }
    }

    /// <summary>
    /// 只由时间决定的弹簧值：value(t) = base + Σ Δᵢ·step(t − t₀ᵢ) + Σ vⱼ·impulse(t − t₀ⱼ)。
    /// 帧与帧之间不保存插值状态，中途改目标时位置和速度天然连续
    /// </summary>
    public sealed class SpringValue
    {
        private struct Segment
        {
            public double Start;
            public double Amount;
            public bool IsImpulse;
            public Spring Spring;
        }

        private readonly List<Segment> segments = new List<Segment>();
        private double baseValue;

        public SpringValue(double value, Spring spring)
        {
            baseValue = value;
            Target = value;
            Spring = spring;
        }

        public SpringValue(double value) : this(value, Spring.Default)
        {
        }

        /// <summary>
        /// 不指定时使用的弹簧
        /// </summary>
        public Spring Spring { get; set; }

        public double Target { get; private set; }

        public double Value
        {
            get { return ValueAt(Motion.Now); }
        }

        public double ValueAt(double now)
        {
            Prune(now);
            double value = baseValue;
            foreach (Segment s in segments)
            {
                double t = now - s.Start;
                value += s.IsImpulse ? s.Amount * s.Spring.Impulse(t) : s.Amount * (s.Spring.Step(t) - 1);
            }
            // 阶跃段在 base 中已计入终值，这里加的是“尚未走完的部分”（step − 1）
            return value;
        }

        public double VelocityAt(double now)
        {
            double velocity = 0;
            foreach (Segment s in segments)
            {
                double t = now - s.Start;
                velocity += s.IsImpulse ? s.Amount * s.Spring.ImpulseVelocity(t) : s.Amount * s.Spring.StepVelocity(t);
            }
            return velocity;
        }

        /// <summary>
        /// 改目标：追加一段 Δ = 新目标 − 旧目标，起点为 at
        /// </summary>
        public void Set(double target, double at, Spring spring)
        {
            double delta = target - Target;
            if (delta == 0) return;
            Target = target;
            baseValue += delta;
            segments.Add(new Segment { Start = at, Amount = delta, Spring = spring });
        }

        public void Set(double target, double at)
        {
            Set(target, at, Spring);
        }

        public void Set(double target)
        {
            Set(target, Motion.Now, Spring);
        }

        /// <summary>
        /// 直接跳到某个值并静止（拖动时跟手）
        /// </summary>
        public void Snap(double value)
        {
            segments.Clear();
            baseValue = value;
            Target = value;
        }

        /// <summary>
        /// 叠加一个初速度（像素/秒），终点不变
        /// </summary>
        public void Kick(double velocity, double at, Spring spring)
        {
            if (velocity == 0) return;
            segments.Add(new Segment { Start = at, Amount = velocity, IsImpulse = true, Spring = spring });
        }

        public void Kick(double velocity, double at)
        {
            Kick(velocity, at, Spring);
        }

        public bool IsAnimatingAt(double now)
        {
            Prune(now);
            return segments.Count > 0;
        }

        public bool IsAnimating
        {
            get { return IsAnimatingAt(Motion.Now); }
        }

        /// <summary>
        /// 已经静止的段并入 base
        /// </summary>
        private void Prune(double now)
        {
            for (int i = segments.Count - 1; i >= 0; i--)
            {
                Segment s = segments[i];
                if (now - s.Start > s.Spring.SettleTime) segments.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// 动画时间源（秒）。可以冻结，用于逐帧截图检查
    /// </summary>
    public static class Motion
    {
        private static readonly Stopwatch Watch = Stopwatch.StartNew();
        private static double? frozen;

        public static double Now
        {
            get { return frozen ?? Watch.Elapsed.TotalSeconds; }
        }

        public static bool IsFrozen
        {
            get { return frozen.HasValue; }
        }

        public static void Freeze(double time)
        {
            frozen = time;
        }

        public static void Unfreeze()
        {
            frozen = null;
        }

        /// <summary>
        /// 0..1 的进度：从 start 开始、持续 duration 秒
        /// </summary>
        public static double Progress(double now, double start, double duration)
        {
            if (duration <= 0) return now >= start ? 1 : 0;
            double p = (now - start) / duration;
            return p < 0 ? 0 : p > 1 ? 1 : p;
        }

        public static double EaseOutCubic(double p)
        {
            double q = 1 - p;
            return 1 - q * q * q;
        }

        public static double Clamp01(double v)
        {
            return v < 0 ? 0 : v > 1 ? 1 : v;
        }

        public static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }
    }

    /// <summary>
    /// 模糊交叉淡化：旧内容 110ms 内淡出、模糊到 4px、上移 3px；新内容延迟 80ms 后 170ms 淡入落位。
    /// 快速连续切换时更早的旧内容继续淡出，不会闪
    /// </summary>
    public sealed class Crossfade<T>
    {
        public const double OutDuration = 0.110;
        public const double InDelay = 0.080;
        public const double InDuration = 0.170;
        public const double MaxBlur = 4;
        public const double Shift = 3;

        private readonly List<KeyValuePair<T, double>> outgoing = new List<KeyValuePair<T, double>>();
        private double changedAt = double.NegativeInfinity;

        public Crossfade(T initial)
        {
            Current = initial;
        }

        public T Current { get; private set; }

        /// <summary>
        /// 切换内容；与当前相同时什么都不做
        /// </summary>
        public bool Set(T value, double now)
        {
            if (EqualityComparer<T>.Default.Equals(value, Current)) return false;
            // 当前内容如果还没完全淡入，就从此刻开始淡出
            outgoing.Add(new KeyValuePair<T, double>(Current, now));
            Current = value;
            changedAt = now;
            return true;
        }

        /// <summary>
        /// 不做动画直接换掉
        /// </summary>
        public void Reset(T value)
        {
            outgoing.Clear();
            Current = value;
            changedAt = double.NegativeInfinity;
        }

        public bool IsAnimatingAt(double now)
        {
            outgoing.RemoveAll(o => now - o.Value > OutDuration);
            return outgoing.Count > 0 || now - changedAt < InDelay + InDuration;
        }

        /// <summary>
        /// 依次给出每一层：内容、不透明度、模糊半径、纵向偏移
        /// </summary>
        public IEnumerable<Layer> Layers(double now)
        {
            outgoing.RemoveAll(o => now - o.Value > OutDuration);
            foreach (KeyValuePair<T, double> o in outgoing)
            {
                double p = Motion.Progress(now, o.Value, OutDuration);
                yield return new Layer(o.Key, 1 - p, MaxBlur * p, -Shift * p);
            }
            double q = Motion.EaseOutCubic(Motion.Progress(now, changedAt + InDelay, InDuration));
            yield return new Layer(Current, q, MaxBlur * (1 - q), Shift * (1 - q));
        }

        public struct Layer
        {
            public Layer(T value, double opacity, double blur, double offsetY)
            {
                Value = value;
                Opacity = opacity;
                Blur = blur;
                OffsetY = offsetY;
            }

            public T Value { get; private set; }
            public double Opacity { get; private set; }
            public double Blur { get; private set; }
            public double OffsetY { get; private set; }
        }
    }
}
