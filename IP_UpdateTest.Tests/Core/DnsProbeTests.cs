using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class DnsProbeTests
    {
        [Fact]
        public void BuildQuery_EncodesHeaderAndName()
        {
            byte[] query = DnsProbe.BuildQuery(0x1234, "www.qq.com");

            Assert.Equal(new byte[]
            {
                0x12, 0x34, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                3, (byte)'w', (byte)'w', (byte)'w', 2, (byte)'q', (byte)'q', 3, (byte)'c', (byte)'o', (byte)'m', 0,
                0x00, 0x01, 0x00, 0x01
            }, query);
        }

        [Fact]
        public void BuildQuery_RejectsEmptyLabel()
        {
            Assert.Throws<System.ArgumentException>(() => DnsProbe.BuildQuery(1, "www..com"));
        }

        [Theory]
        [InlineData(0x12, 0x34, 0x81, 0x80, true)]  // 正常应答
        [InlineData(0x12, 0x34, 0x81, 0x83, true)]  // NXDOMAIN：服务器同样在工作
        [InlineData(0x12, 0x34, 0x81, 0x82, false)] // SERVFAIL
        [InlineData(0x12, 0x34, 0x01, 0x00, false)] // 不是应答（QR=0）
        [InlineData(0x43, 0x21, 0x81, 0x80, false)] // ID 不一致
        public void IsResponseTo(byte idHigh, byte idLow, byte flags1, byte flags2, bool expected)
        {
            byte[] response = { idHigh, idLow, flags1, flags2, 0, 1, 0, 1, 0, 0, 0, 0 };

            Assert.Equal(expected, DnsProbe.IsResponseTo(response, 0x1234));
        }

        [Fact]
        public void IsResponseTo_TooShort()
        {
            Assert.False(DnsProbe.IsResponseTo(new byte[] { 0x12, 0x34, 0x81 }, 0x1234));
            Assert.False(DnsProbe.IsResponseTo(null, 0x1234));
        }
    }
}
