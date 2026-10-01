using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class WmiReturnCodeTests
    {
        [Theory]
        [InlineData("EnableStatic", 0u)]
        [InlineData("EnableStatic", 1u)]
        [InlineData("SetGateways", 0u)]
        [InlineData("SetDNSServerSearchOrder", 1u)]
        [InlineData("EnableDHCP", 0u)]
        public void IsSuccess_ZeroAndOne_AreSuccess(string method, uint code)
        {
            Assert.True(WmiReturnCode.IsSuccess(method, code));
        }

        [Fact]
        public void IsSuccess_EnableStatic81_IsSuccess()
        {
            // 网卡已是静态地址时 EnableStatic 返回 81，但设置实际已生效
            Assert.True(WmiReturnCode.IsSuccess("EnableStatic", 81));
        }

        [Theory]
        [InlineData("SetGateways")]
        [InlineData("SetDNSServerSearchOrder")]
        [InlineData("EnableDHCP")]
        public void IsSuccess_81OnOtherMethods_IsFailure(string method)
        {
            Assert.False(WmiReturnCode.IsSuccess(method, 81));
        }

        [Theory]
        [InlineData(70u)]
        [InlineData(84u)]
        [InlineData(91u)]
        public void IsSuccess_ErrorCodes_AreFailure(uint code)
        {
            Assert.False(WmiReturnCode.IsSuccess("EnableStatic", code));
        }

        [Fact]
        public void Describe_AccessDenied_MentionsAdministratorAndCode()
        {
            string text = WmiReturnCode.Describe(91);

            Assert.Contains("管理员", text);
            Assert.Contains("91", text);
        }

        [Fact]
        public void Describe_WriteLockCode_IsKnown()
        {
            Assert.Contains("写锁", WmiReturnCode.Describe(2147786788));
        }

        [Fact]
        public void Describe_UnknownCode_FallsBackWithCode()
        {
            Assert.Equal("未知错误（错误码 12345）", WmiReturnCode.Describe(12345));
        }
    }
}
