using System.Text.Json;

namespace LawReview.Core.LandUse;

/// <summary>
/// 브이월드(VWorld) 국토정보플랫폼 클라이언트 — 토지이음 색인 용도.
///
/// 역할은 "그 땅에 원래 딸린 사실"을 가져오는 것까지다
/// (검토 품질 원칙 2 — 개략 검토 내용은 검토서에 인용 금지):
///  (a) 지번 주소 → PNU(19자리 필지고유번호)
///  (b) PNU → 용도지역·지구·구역 이름 목록 (지역/지구 입력란 자동 채움용 색인)
///  (c) PNU → 토지대장 (대지면적·지목·법정동명)
/// 건축면적·연면적·층수는 설계 결과물이라 조회 대상이 아니다 — 사용자가 입력한다.
///
/// 키는 www.vworld.kr에서 무료 발급하며, 발급 시 등록한 서비스 URL(domain)과 일치해야
/// NED API가 동작한다. 엔드포인트·응답 필드는 실호출로 검증됨 (2026-07-19).
/// </summary>
public sealed class VworldClient
{
    private readonly HttpClient _http;
    private readonly string _key;
    private readonly string _domain;

    public VworldClient(HttpClient http, string key, string domain = "http://localhost")
    {
        _http = http;
        _key = key;
        _domain = domain;
    }

    /// <summary>지번 주소 → PNU. 도로명 주소는 인식되지 않을 수 있다.</summary>
    public async Task<PnuResult?> AddressToPnuAsync(string address, CancellationToken ct = default)
    {
        var uri = new Uri("https://api.vworld.kr/req/address?service=address&request=getCoord&type=PARCEL" +
                          $"&key={Uri.EscapeDataString(_key)}&address={Uri.EscapeDataString(address)}");
        using var doc = JsonDocument.Parse(await GetStringAsync(uri, ct));
        return ParsePnu(doc.RootElement);
    }

    internal static PnuResult? ParsePnu(JsonElement root)
    {
        if (!root.TryGetProperty("response", out var resp)) return null;
        if (GetString(resp, "status") != "OK") return null;
        if (!resp.TryGetProperty("refined", out var refined)) return null;
        var pnu = refined.TryGetProperty("structure", out var structure) ? GetString(structure, "level4LC") : null;
        if (pnu is null || pnu.Length != 19) return null;
        return new PnuResult(pnu, GetString(refined, "text") ?? "");
    }

    /// <summary>PNU → 용도지역·지구·구역 이름 목록 (해당 필지에 "포함"된 것만, cnflcAt=1).</summary>
    public async Task<IReadOnlyList<string>> GetLandUseZonesAsync(string pnu, CancellationToken ct = default)
    {
        var uri = new Uri($"https://api.vworld.kr/ned/data/getLandUseAttr?pnu={Uri.EscapeDataString(pnu)}" +
                          $"&cnflcAt=1&key={Uri.EscapeDataString(_key)}&domain={Uri.EscapeDataString(_domain)}");
        using var doc = JsonDocument.Parse(await GetStringAsync(uri, ct));
        return ParseZones(doc.RootElement);
    }

    internal static IReadOnlyList<string> ParseZones(JsonElement root)
    {
        var results = new List<string>();
        if (!root.TryGetProperty("landUses", out var landUses)) return results;
        if (!landUses.TryGetProperty("field", out var field)) return results;

        var items = field.ValueKind == JsonValueKind.Array
            ? field.EnumerateArray().ToList()
            : new List<JsonElement> { field };
        foreach (var item in items)
            if (GetString(item, "prposAreaDstrcCodeNm") is { Length: > 0 } name && !results.Contains(name))
                results.Add(name);
        return results;
    }

    /// <summary>PNU → 토지대장 (대지면적·지목·법정동명). 없으면 null.</summary>
    public async Task<LandRegister?> GetLandRegisterAsync(string pnu, CancellationToken ct = default)
    {
        var uri = new Uri($"https://api.vworld.kr/ned/data/ladfrlList?pnu={Uri.EscapeDataString(pnu)}" +
                          $"&key={Uri.EscapeDataString(_key)}&domain={Uri.EscapeDataString(_domain)}");
        using var doc = JsonDocument.Parse(await GetStringAsync(uri, ct));
        return ParseLandRegister(doc.RootElement);
    }

    internal static LandRegister? ParseLandRegister(JsonElement root)
    {
        if (!root.TryGetProperty("ladfrlVOList", out var outer)) return null;
        if (!outer.TryGetProperty("ladfrlVOList", out var inner)) return null;

        var item = inner.ValueKind == JsonValueKind.Array
            ? inner.EnumerateArray().FirstOrDefault()
            : inner;
        if (item.ValueKind != JsonValueKind.Object) return null;

        // 면적은 문자열("6030.1")로 온다. 지역 설정과 무관하게 파싱되도록 InvariantCulture 사용.
        double? area = null;
        if (GetString(item, "lndpclAr") is { Length: > 0 } raw
            && double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            area = parsed;

        return new LandRegister(
            Area: area,
            Category: GetString(item, "lndcgrCodeNm") ?? "",       // 지목 (예: 공장용지)
            LegalDongName: GetString(item, "ldCodeNm") ?? "");     // 예: 대전광역시 유성구 둔곡동
    }

    /// <summary>
    /// 주소 한 번으로 땅 정보 색인 조회: PNU + 용도지역 목록 + 대지면적·지목 + 광역/기초 지자체명.
    /// 토지대장 조회가 실패해도 용도지역까지는 반환한다 (부분 성공 허용).
    /// </summary>
    public async Task<LandUseIndex?> GetLandUseIndexAsync(string address, CancellationToken ct = default)
    {
        var pnu = await AddressToPnuAsync(address, ct);
        if (pnu is null) return null;
        var zones = await GetLandUseZonesAsync(pnu.Pnu, ct);

        LandRegister? register = null;
        try
        {
            register = await GetLandRegisterAsync(pnu.Pnu, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            // 대지면적은 있으면 좋은 정보지 필수는 아니다 — 용도지역 결과를 살린다.
        }

        // 지자체명은 토지대장의 법정동명이 가장 정확하고, 없으면 정제 주소로 대체한다.
        var (province, city) = SplitMunicipality(
            register?.LegalDongName is { Length: > 0 } dong ? dong : pnu.RefinedAddress);

        return new LandUseIndex(pnu.Pnu, pnu.RefinedAddress, zones,
            register?.Area, register?.Category ?? "", province, city);
    }

    /// <summary>
    /// 법정동명/주소에서 광역·기초 지자체명을 뽑는다.
    /// "대전광역시 유성구 둔곡동" → (대전광역시, 유성구)
    /// "경기도 성남시 분당구 정자동" → (경기도, 성남시 분당구)  ← 조례는 "성남시 ○○ 조례"이므로 시+구를 함께 둔다
    /// "세종특별자치시 어진동" → (세종특별자치시, 세종특별자치시)  ← 기초 지자체가 없는 단층제
    /// </summary>
    internal static (string Province, string City) SplitMunicipality(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ("", "");

        var province = parts[0];
        // 단층제(세종·제주 일부)는 기초 지자체가 따로 없다.
        if (parts.Length == 1 || province.StartsWith("세종")) return (province, province);

        // 도 산하 "○○시 ○○구"는 두 토큰이 함께 기초 지자체를 이룬다 (성남시 분당구 등).
        if (parts.Length >= 3 && parts[1].EndsWith('시') && (parts[2].EndsWith('구') || parts[2].EndsWith('군')))
            return (province, $"{parts[1]} {parts[2]}");

        return (province, parts[1]);
    }

    private async Task<string> GetStringAsync(Uri uri, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(uri, ct);
        resp.EnsureSuccessStatusCode();
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (text.TrimStart().StartsWith('<'))
            throw new InvalidOperationException(
                "VWorld API가 JSON 대신 다른 응답을 반환했습니다. 키와 등록 도메인(서비스 URL)을 확인하세요.");
        return text;
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}

public sealed record PnuResult(string Pnu, string RefinedAddress);

/// <summary>토지대장 정보 (그 땅에 딸린 사실).</summary>
public sealed record LandRegister(double? Area, string Category, string LegalDongName);

/// <summary>
/// 토지이음 색인 결과. 용도지역 이름·대지면적 등 "땅에 딸린 사실"만 담는다 —
/// 개략 검토 내용은 여기 실리지 않는다 (원칙 2).
/// </summary>
public sealed record LandUseIndex(
    string Pnu,
    string RefinedAddress,
    IReadOnlyList<string> Zones,
    double? Area = null,
    string Category = "",
    string Province = "",
    string City = "");
