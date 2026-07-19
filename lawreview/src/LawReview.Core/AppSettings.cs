using System.Text.Json;

namespace LawReview.Core;

/// <summary>
/// 사용자별 설정. 협력체 배포 원칙: API 키는 각 사용자가 직접 발급해 입력하고
/// 해당 PC(%APPDATA%\LawReview\settings.json)에만 저장한다. exe에 키를 심지 않는다.
/// </summary>
public sealed class AppSettings
{
    public string MolegApiKey { get; set; } = "";      // 법제처 Open API OC 키 (open.law.go.kr, 무료)
    public string ClaudeApiKey { get; set; } = "";     // Anthropic API 키 (판정용, 호출당 과금)
    public string ClaudeModel { get; set; } = "claude-sonnet-5";
    public string VworldApiKey { get; set; } = "";     // VWorld 키 (www.vworld.kr, 무료) — 용도지역 자동조회
    public string VworldDomain { get; set; } = "http://localhost";  // VWorld 키 발급 시 등록한 서비스 URL

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LawReview");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
