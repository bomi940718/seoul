using System.Text.Json;

namespace LawReview.Core.LandUse;

/// <summary>
/// 브이월드(VWorld) 국토정보플랫폼 클라이언트 — 토지이음 색인 용도.
///
/// 역할은 딱 두 가지다 (검토 품질 원칙 2 — 개략 검토 내용은 검토서에 인용 금지):
///  (a) 지번 주소 → PNU(19자리 필지고유번호)
///  (b) PNU → 용도지역·지구·구역 이름 목록 (지역/지구 입력란 자동 채움용 색인)
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

    /// <summary>주소 한 번으로 색인 조회: PNU + 용도지역 목록.</summary>
    public async Task<LandUseIndex?> GetLandUseIndexAsync(string address, CancellationToken ct = default)
    {
        var pnu = await AddressToPnuAsync(address, ct);
        if (pnu is null) return null;
        var zones = await GetLandUseZonesAsync(pnu.Pnu, ct);
        return new LandUseIndex(pnu.Pnu, pnu.RefinedAddress, zones);
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

/// <summary>토지이음 색인 결과. 용도지역 이름만 담는다 — 개략 검토 내용은 여기 실리지 않는다.</summary>
public sealed record LandUseIndex(string Pnu, string RefinedAddress, IReadOnlyList<string> Zones);
