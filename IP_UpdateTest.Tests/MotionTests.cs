using System;
using System.Linq;
using IP_UpdateTest.Ui;
using Xunit;
using static IP_UpdateTest.Tests.Approx;

namespace IP_UpdateTest.Tests
{
    internal static class Approx
    {
        public static void Near(double expected, double actual, double tolerance)
        {
            Assert.True(Math.Abs(expected - actual) <= tolerance, $"期望 {expected}，实际 {actual}");
        }
    }

    public class SpringTests
    {
        public static readonly object[][] Presets =
        {
            new object[] { "snappy", 0.26, 0.86 },
            new object[] { "default", 0.38, 0.82 },
            new object[] { "smooth", 0.50, 0.86 },
            new object[] { "lead", 0.26, 0.84 },
            new object[] { "trail", 0.46, 0.86 }
        };

        [Theory]
        [MemberData(nameof(Presets))]
        public void Step_StartsAtZeroAndSettlesAtOne(string name, double response, double damping)
        {
            var spring = new Spring(response, damping);

            Assert.Equal(0, spring.Step(0));
            Assert.Equal(0, spring.StepVelocity(0));
            Assert.InRange(spring.Step(spring.SettleTime), 1 - 1e-3, 1 + 1e-3);
            Assert.InRange(Math.Abs(spring.StepVelocity(spring.SettleTime)), 0, 1e-2);
            // 接近临界阻尼：过冲很小，不会来回弹
            double peak = Enumerable.Range(0, 2000).Select(i => spring.Step(i * spring.SettleTime / 2000)).Max();
            Assert.True(peak < 1.02, name + " 过冲 " + peak);
        }

        [Fact]
        public void Velocity_MatchesNumericDerivative()
        {
            var spring = Spring.Default;
            const double h = 1e-6;
            foreach (double t in new[] { 0.01, 0.05, 0.1, 0.2, 0.4 })
            {
                double numeric = (spring.Step(t + h) - spring.Step(t - h)) / (2 * h);
                Near(numeric, spring.StepVelocity(t), 1e-2);
                double impulse = (spring.Impulse(t + h) - spring.Impulse(t - h)) / (2 * h);
                Near(impulse, spring.ImpulseVelocity(t), 1e-2);
            }
        }

        [Fact]
        public void Impulse_StartsWithUnitVelocityAndReturnsToZero()
        {
            var spring = Spring.Snappy;

            Near(1, spring.ImpulseVelocity(0), 1e-8);
            Assert.InRange(Math.Abs(spring.Impulse(spring.SettleTime)), 0, 1e-4);
        }

        [Fact]
        public void Shake_IsUnderdampedAndShort()
        {
            var spring = Spring.Shake;
            // 抖动要来回几下，但很快停下
            Assert.True(spring.Step(0.1) > 1, "应当过冲");
            Assert.True(spring.SettleTime < 1.5);
        }
    }

    public class SpringValueTests
    {
        [Fact]
        public void Set_MovesTowardTargetAndStops()
        {
            var value = new SpringValue(0, Spring.Default);

            value.Set(100, 10);

            Near(0, value.ValueAt(10), 1e-8);
            Assert.InRange(value.ValueAt(10.1), 1, 99);
            Assert.True(value.IsAnimatingAt(10.1));
            Near(100, value.ValueAt(10 + Spring.Default.SettleTime + 0.01), 1e-8);
            Assert.False(value.IsAnimatingAt(10 + Spring.Default.SettleTime + 0.01));
        }

        [Fact]
        public void Retarget_KeepsPositionAndVelocityContinuous()
        {
            var value = new SpringValue(0, Spring.Default);
            value.Set(100, 0);
            double before = value.ValueAt(0.08);
            double velocityBefore = value.VelocityAt(0.08);

            value.Set(-50, 0.08);

            Near(before, value.ValueAt(0.08), 1e-8);
            Near(velocityBefore, value.VelocityAt(0.08), 1e-5);
            Assert.Equal(-50, value.Target);
            Near(-50, value.ValueAt(5), 1e-5);
        }

        [Fact]
        public void Kick_AddsVelocityWithoutChangingTarget()
        {
            var value = new SpringValue(20, Spring.Snappy);

            value.Kick(300, 1);

            Near(20, value.ValueAt(1), 1e-8);
            Near(300, value.VelocityAt(1), 1e-5);
            Assert.True(value.ValueAt(1.03) > 20);
            Near(20, value.ValueAt(3), 1e-5);
        }

        [Fact]
        public void Snap_JumpsAndStops()
        {
            var value = new SpringValue(0, Spring.Smooth);
            value.Set(1, 0);

            value.Snap(0.4);

            Assert.Equal(0.4, value.ValueAt(0.1));
            Assert.False(value.IsAnimatingAt(0.1));
        }
    }

    public class CrossfadeTests
    {
        [Fact]
        public void Set_FadesOldOutBeforeNewComesIn()
        {
            var fade = new Crossfade<string>("旧");

            fade.Set("新", 0);

            var start = fade.Layers(0).ToList();
            Assert.Equal(2, start.Count);
            Assert.Equal("旧", start[0].Value);
            Near(1, start[0].Opacity, 1e-5);
            Near(0, start[1].Opacity, 1e-5);
            Near(Crossfade<string>.MaxBlur, start[1].Blur, 1e-5);

            // 80ms 时新内容还没开始出现
            var mid = fade.Layers(0.08).ToList();
            Near(0, mid.Last().Opacity, 1e-5);

            var end = fade.Layers(0.3).ToList();
            Assert.Single(end);
            Assert.Equal("新", end[0].Value);
            Near(1, end[0].Opacity, 1e-5);
            Near(0, end[0].Blur, 1e-5);
            Assert.False(fade.IsAnimatingAt(0.3));
        }

        [Fact]
        public void Set_SameValue_DoesNothing()
        {
            var fade = new Crossfade<string>("a");

            Assert.False(fade.Set("a", 0));
            Assert.False(fade.IsAnimatingAt(0));
        }
    }
}
