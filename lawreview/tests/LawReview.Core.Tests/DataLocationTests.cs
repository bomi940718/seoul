using Xunit;

namespace LawReview.Core.Tests;

/// <summary>
/// 스토어(MSIX) 앱 가상 폴더에만 남은 설정을 가져오는지 검증.
///
/// 실제로 겪은 일(2026-11): Claude Desktop 같은 스토어 앱 안에서 이 프로그램을 띄워 키를 넣으면
/// 키가 <c>%LOCALAPPDATA%\Packages\{앱}\LocalCache\Roaming\LawReview</c>에만 저장되고,
/// 탐색기에서 exe를 더블클릭하면 빈 설정으로 떠서 "주소 조회가 안 된다"로 보였다.
/// </summary>
public class DataLocationTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "lawreview_dl_" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(_temp, "Roaming", "LawReview");
    private string Packages => Path.Combine(_temp, "Packages");

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }

    private string MakePackageCopy(string package, string settings, DateTime when, params string[] projects)
    {
        var dir = Path.Combine(Packages, package, "LocalCache", "Roaming", "LawReview");
        Directory.CreateDirectory(Path.Combine(dir, "projects"));
        var file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, settings);
        File.SetLastWriteTimeUtc(file, when);
        foreach (var p in projects) File.WriteAllText(Path.Combine(dir, "projects", p + ".json"), "{}");
        return dir;
    }

    [Fact]
    public void 실제_폴더에_설정이_없으면_가상_폴더의_설정과_프로젝트를_가져온다()
    {
        MakePackageCopy("Claude_pzs8sxrjxfjjc", """{"VworldApiKey":"abc"}""", DateTime.UtcNow, "세이퍼존 둔곡 공장");

        var source = DataLocation.TryImportFromPackages(Root, Packages);

        Assert.NotNull(source);
        Assert.Contains("abc", File.ReadAllText(Path.Combine(Root, "settings.json")));
        Assert.True(File.Exists(Path.Combine(Root, "projects", "세이퍼존 둔곡 공장.json")));
    }

    [Fact]
    public void 실제_폴더에_설정이_있으면_건드리지_않는다()
    {
        // 사용자가 실제 폴더에서 고친 값이 이긴다.
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "settings.json"), """{"VworldApiKey":"mine"}""");
        MakePackageCopy("Claude_x", """{"VworldApiKey":"old"}""", DateTime.UtcNow);

        Assert.Null(DataLocation.TryImportFromPackages(Root, Packages));
        Assert.Contains("mine", File.ReadAllText(Path.Combine(Root, "settings.json")));
    }

    [Fact]
    public void 여러_스토어_앱에_사본이_있으면_가장_최근_것을_가져온다()
    {
        MakePackageCopy("A_app", """{"VworldApiKey":"older"}""", DateTime.UtcNow.AddDays(-30));
        MakePackageCopy("B_app", """{"VworldApiKey":"newer"}""", DateTime.UtcNow);

        DataLocation.TryImportFromPackages(Root, Packages);

        Assert.Contains("newer", File.ReadAllText(Path.Combine(Root, "settings.json")));
    }

    [Fact]
    public void 이미_있는_프로젝트는_덮어쓰지_않는다()
    {
        Directory.CreateDirectory(Path.Combine(Root, "projects"));
        File.WriteAllText(Path.Combine(Root, "projects", "공장.json"), "MINE");
        MakePackageCopy("Claude_x", "{}", DateTime.UtcNow, "공장");

        DataLocation.TryImportFromPackages(Root, Packages);

        Assert.Equal("MINE", File.ReadAllText(Path.Combine(Root, "projects", "공장.json")));
    }

    [Fact]
    public void 일반_PC에서는_아무_일도_일어나지_않는다()
    {
        // 협력사 PC — 스토어 앱 가상 폴더에 사본이 없다.
        Directory.CreateDirectory(Path.Combine(Packages, "Microsoft.WindowsStore_8wekyb3d8bbwe"));

        Assert.Null(DataLocation.TryImportFromPackages(Root, Packages));
        Assert.False(Directory.Exists(Root));
        Assert.Null(DataLocation.TryImportFromPackages(Root, Path.Combine(_temp, "없는폴더")));
    }
}
