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
    public string ClaudeModel { get; set; } = DefaultClaudeModel;
    public string VworldApiKey { get; set; } = "";     // VWorld 키 (www.vworld.kr, 무료) — 용도지역 자동조회
    public string VworldDomain { get; set; } = "http://localhost";  // VWorld 키 발급 시 등록한 서비스 URL
    /// <summary>공공데이터포털 일반 인증키 (data.go.kr, 무료) — 건축물대장(건축HUB) 조회. 계정당 하나.</summary>
    public string DataGoKrApiKey { get; set; } = "";

    /// <summary>판정에 쓰는 Claude 모델의 기본값.</summary>
    public const string DefaultClaudeModel = "claude-sonnet-5-5";

    /// <summary>
    /// 예전 기본값들. 설정 파일에는 모델이 늘 함께 저장되므로(설정 창이 모델 칸까지 저장한다),
    /// 이 값이 들어 있으면 사용자가 고른 게 아니라 **그때의 기본값**이다 — 불러올 때 현재 기본값으로 올린다.
    /// 이렇게 하지 않으면 기본값을 바꿔도 이미 키를 저장한 PC는 계속 옛 모델을 쓴다.
    /// </summary>
    private static readonly string[] LegacyDefaultModels = { "claude-sonnet-5" };

    // 경로는 DataLocation이 정한다 — 스토어 앱 가상 폴더에만 남은 설정을 가져오는 일까지 맡는다.
    private static string Dir => DataLocation.Root;
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        var s = File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : new AppSettings();
        return WithBundledDefaults(s, Bundled.Value);
    }

    /// <summary>
    /// 공유용 exe(소우주 배포, build-share.bat)에 함께 묶인 키. 일반 빌드에는 없다(null).
    /// 사용자가 받자마자 쓸 수 있게 하려는 것 — 2026-11-17 사용자 결정("NAS 설치 파일엔 내가 쓰던 키를 그대로").
    /// 키 원본은 저장소 밖(%USERPROFILE%\.lawreview\share-keys.json)에 있고 저장소에는 절대 들어가지 않는다.
    /// </summary>
    private static readonly Lazy<AppSettings?> Bundled = new(() =>
    {
        using var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("bundled-settings.json");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    });

    /// <summary>비어 있는 키만 묶인 키로 채운다 — 사람이 직접 넣은 키가 이긴다.</summary>
    internal static AppSettings WithBundledDefaults(AppSettings s, AppSettings? bundled)
    {
        if (bundled is null) return s;
        if (s.MolegApiKey.Length == 0) s.MolegApiKey = bundled.MolegApiKey;
        if (s.ClaudeApiKey.Length == 0) s.ClaudeApiKey = bundled.ClaudeApiKey;
        if (s.VworldApiKey.Length == 0)
        {
            s.VworldApiKey = bundled.VworldApiKey;
            s.VworldDomain = bundled.VworldDomain;   // VWorld 키는 등록한 서비스 URL과 짝이다
        }
        if (s.DataGoKrApiKey.Length == 0) s.DataGoKrApiKey = bundled.DataGoKrApiKey;
        return s;
    }

    internal static AppSettings Parse(string json)
    {
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            if (LegacyDefaultModels.Contains(s.ClaudeModel)) s.ClaudeModel = DefaultClaudeModel;
            return s;
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
