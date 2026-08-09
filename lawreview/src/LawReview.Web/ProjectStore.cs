using System.Text.Json;

namespace LawReview.Web;

/// <summary>
/// 작업 중인 프로젝트를 이 PC에 저장한다(%APPDATA%\LawReview\projects).
/// 화면 상태(설계개요 입력·면적표·검토 결과)를 그대로 담으므로 스키마는 화면이 정한다.
/// </summary>
public static class ProjectStore
{
    private static string Dir
    {
        get
        {
            var d = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LawReview", "projects");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>파일명으로 쓸 수 없는 문자를 걸러낸다(경로 탈출 방지 포함).</summary>
    internal static string SafeName(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return "";
        foreach (var c in Path.GetInvalidFileNameChars()) trimmed = trimmed.Replace(c, '_');
        trimmed = trimmed.Replace("..", "_");
        return trimmed.Length > 120 ? trimmed[..120] : trimmed;
    }

    private static string PathOf(string name) => Path.Combine(Dir, SafeName(name) + ".json");

    public static IEnumerable<object> List() =>
        new DirectoryInfo(Dir).GetFiles("*.json")
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new
            {
                name = Path.GetFileNameWithoutExtension(f.Name),
                savedAt = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                size = f.Length,
            });

    public static void Save(string name, JsonElement data)
    {
        var safe = SafeName(name);
        if (safe.Length == 0) throw new ArgumentException("프로젝트 이름을 입력하세요.");
        File.WriteAllText(PathOf(safe), data.GetRawText());
    }

    public static string? Load(string name)
    {
        var p = PathOf(name);
        return File.Exists(p) ? File.ReadAllText(p) : null;
    }

    public static bool Delete(string name)
    {
        var p = PathOf(name);
        if (!File.Exists(p)) return false;
        File.Delete(p);
        return true;
    }
}
