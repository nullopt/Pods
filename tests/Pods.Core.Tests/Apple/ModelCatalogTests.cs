using Pods.Core.Apple;
namespace Pods.Core.Tests.Apple;

public class ModelCatalogTests
{
    [Fact] public void Catalog_Has31Models() => Assert.Equal(31, ModelCatalog.All.Count);

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

    [Fact]
    public void Find_StudioBudsOnlyLidPopup() =>
        Assert.Equal(AppleCapabilities.LidPopup, ModelCatalog.Find("1120")!.Capabilities);

    [Fact]
    public void Find_BeatsFlexHasNoCapabilities() =>
        Assert.Equal(AppleCapabilities.None, ModelCatalog.Find("1020")!.Capabilities);

    [Fact] public void Find_UnknownReturnsNull() => Assert.Null(ModelCatalog.Find("ffff"));
}
