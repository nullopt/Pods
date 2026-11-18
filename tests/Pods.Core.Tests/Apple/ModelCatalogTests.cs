using Pods.Core.Apple;
namespace Pods.Core.Tests.Apple;

public class ModelCatalogTests
{
    [Fact] public void Catalog_Has14AirPodsModels()
    {
        Assert.Equal(14, ModelCatalog.All.Count);
        Assert.All(ModelCatalog.All, m => Assert.StartsWith("AirPods", m.Name));
    }

    [Fact]
    public void Find_ProTwoHasAllCapabilities()
    {
        var m = ModelCatalog.Find("1420")!;
        Assert.Equal("AirPods Pro 2", m.Name);
        Assert.Equal(AppleCapabilities.EarDetection | AppleCapabilities.LidPopup | AppleCapabilities.AutoConnect, m.Capabilities);
    }

    [Fact]
    public void Find_MaxHasNoAutoConnect() =>
        Assert.Equal(AppleCapabilities.EarDetection | AppleCapabilities.LidPopup, ModelCatalog.Find("0a20")!.Capabilities);

    [Theory]
    [InlineData("0b20")]   // Powerbeats Pro
    [InlineData("1120")]   // Beats Studio Buds
    [InlineData("0620")]   // Beats Solo3
    [InlineData("1020")]   // Beats Flex
    public void Find_BeatsNotCatalogued(string modelId) => Assert.Null(ModelCatalog.Find(modelId));

    [Fact]
    public void SingleBattery_IsTheMaxFamily() => Assert.True(ModelCatalog.SingleBatteryIds.SetEquals(ModelCatalog.MaxFamilyIds));

    [Fact] public void Find_UnknownReturnsNull() => Assert.Null(ModelCatalog.Find("ffff"));
}
