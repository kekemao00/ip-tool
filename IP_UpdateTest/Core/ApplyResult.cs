namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 网络配置操作的结果
    /// </summary>
    public sealed class ApplyResult
    {
        public bool Success { get; private set; }

        /// <summary>
        /// 失败原因或附加说明，可直接展示给用户
        /// </summary>
        public string Message { get; private set; }

        public static ApplyResult Ok(string message = null)
        {
            return new ApplyResult { Success = true, Message = message };
        }

        public static ApplyResult Fail(string message)
        {
            return new ApplyResult { Success = false, Message = message };
        }
    }
}
