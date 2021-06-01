using Xunit;
namespace CasCap.Apis.AzureDevOps.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            if (1 == 1)
                Assert.True(true);
            else
                Assert.True(false);
        }
    }
}