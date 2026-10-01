using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class IpConflictTests
    {
        [Theory]
        [InlineData("192.168.3.53", "255.255.255.0", "192.168.3.88", true)]   // 同网段的新地址
        [InlineData("192.168.3.53", "255.255.255.0", "192.168.3.53", false)]  // 地址没变
        [InlineData("192.168.3.53", "255.255.255.0", "10.0.0.5", false)]      // 跨网段，可能由网关代答
        [InlineData("", "", "192.168.3.88", false)]                            // 网卡当前没有地址
        public void ShouldProbe(string currentIp, string currentMask, string target, bool expected)
        {
            Assert.Equal(expected, IpConflict.ShouldProbe(currentIp, currentMask, target));
        }
    }
}
