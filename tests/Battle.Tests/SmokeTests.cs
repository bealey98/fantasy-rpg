using Battle.Core;

namespace Battle.Tests;

public class SmokeTests
{
    [Test]
    public void RulesVersionIsSet()
    {
        Assert.True(RulesVersion.Current.Split('.').Length == 3, "the rules version should look like 0.1.0");
    }
}
