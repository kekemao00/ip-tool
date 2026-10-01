using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class PendingEditTests
    {
        [Fact]
        public void SaveThenTake_RoundTripsAndDeletes()
        {
            var edit = new PendingEdit
            {
                AdapterId = "{F64D4136-A54B-485D-AE96-742D3946BE16}",
                IsDhcp = false,
                IpAddress = "192.168.1.10",
                SubnetMask = "255.255.255.0",
                Gateway = "192.168.1.1",
                DnsMain = "223.5.5.5",
                DnsBackup = "119.29.29.29"
            };
            edit.Save();

            PendingEdit restored = PendingEdit.Take();

            Assert.NotNull(restored);
            Assert.Equal(edit.AdapterId, restored.AdapterId);
            Assert.Equal("192.168.1.10", restored.IpAddress);
            Assert.Equal("119.29.29.29", restored.DnsBackup);
            Assert.Null(PendingEdit.Take()); // 读取后即删除
        }
    }
}
