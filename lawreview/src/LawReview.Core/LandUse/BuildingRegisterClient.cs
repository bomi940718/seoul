using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LawReview.Core.LandUse;

/// <summary>
/// 건축물대장 조회 — 공공데이터포털 「국토교통부_건축HUB_건축물대장정보 서비스」(데이터 15134735).
///
/// 리모델링·증축·용도변경 검토는 **기존 건축물 값(변경전)** 이 출발점이다. 대장에서
///  (a) 총괄표제부 — 한 필지에 여러 동이 있을 때의 합계 (건축면적·연면적·건폐율·용적률)
///  (b) 표제부     — 동별 개요 (주용도·구조·층수·높이·주차·사용승인일)
///  (c) 층별개요   — 층별 용도·면적 (면적표의 변경전 열)
/// 을 가져온다. 키는 공공데이터포털 계정당 하나이며, 이 서비스를 "활용신청"해야 쓸 수 있다.
///
/// 조회 단위는 필지(PNU)다. PNU 19자리 = 시군구(5) + 법정동(5) + 대지구분(1: 1=대지, 2=산) + 본번(4) + 부번(4).
/// 건축HUB의 대지구분(platGbCd)은 0=대지, 1=산 이라 PNU와 값이 하나씩 어긋난다.
/// </summary>
public sealed class BuildingRegisterClient
{
    private const string BaseUrl = "https://apis.data.go.kr/1613000/BldRgstHubService/";
    private const int PageSize = 100;
    private const int MaxPages = 20;   // 층별개요 2,000행 — 초고층 집합건축물도 이 안에 든다

    private readonly HttpClient _http;
    private readonly string _key;

    public BuildingRegisterClient(HttpClient http, string serviceKey)
    {
        _http = http;
        _key = serviceKey.Trim();
    }

    /// <summary>필지의 건축물대장(총괄표제부·표제부·층별개요). 건축물이 없으면 Buildings가 빈 결과.</summary>
    public async Task<BuildingRegister> GetAsync(string pnu, CancellationToken ct = default)
    {
        var query = ParcelQuery(pnu);
        var recapTask = GetRecapAsync(query, ct);
        var titleTask = GetItemsAsync("getBrTitleInfo", query, ct);
        var floorTask = GetItemsAsync("getBrFlrOulnInfo", query, ct);
        await Task.WhenAll(recapTask, titleTask, floorTask);

        var recap = recapTask.Result;
        var floors = floorTask.Result.Select(ParseFloor).ToList();
        var buildings = titleTask.Result.Select(ParseTitle)
            // 주건축물 먼저, 같은 구분 안에서는 동 이름 순 — 검토서 면적표 순서와 같게
            .OrderBy(b => b.IsAnnex).ThenBy(b => b.DongName, StringComparer.Ordinal)
            .ToList();

        foreach (var b in buildings)
            b.Floors.AddRange(floors
                .Where(f => f.DongName == b.DongName && f.IsAnnex == b.IsAnnex)
                .OrderBy(f => f.SortKey));

        // 표제부에 짝이 없는 층(동 이름 표기가 서로 다른 경우)도 버리지 않는다 — 면적이 빠지면 증감이 틀린다.
        var orphans = floors.Where(f => !buildings.Any(b => b.DongName == f.DongName && b.IsAnnex == f.IsAnnex)).ToList();
        if (orphans.Count > 0)
            foreach (var g in orphans.GroupBy(f => (f.DongName, f.IsAnnex)))
            {
                var b = new RegisterBuilding { DongName = g.Key.DongName, IsAnnex = g.Key.IsAnnex };
                b.Floors.AddRange(g.OrderBy(f => f.SortKey));
                buildings.Add(b);
            }

        return new BuildingRegister(pnu, recap, buildings);
    }

    /// <summary>PNU → 건축HUB 필지 조회 파라미터.</summary>
    internal static string ParcelQuery(string pnu)
    {
        if (pnu.Length != 19 || !pnu.All(char.IsDigit))
            throw new ArgumentException($"PNU는 숫자 19자리여야 합니다: {pnu}", nameof(pnu));
        var platGb = pnu[10] == '2' ? "1" : "0";
        return $"sigunguCd={pnu[..5]}&bjdongCd={pnu[5..10]}&platGbCd={platGb}&bun={pnu[11..15]}&ji={pnu[15..19]}";
    }

    /// <summary>
    /// 총괄표제부는 한 필지에 여러 동이 있을 때만 있고, 이 오퍼레이션만 가끔 503을 낸다(2026-11-16 실측).
    /// 없어도 동별 표제부 합으로 같은 값을 낼 수 있으므로 실패해도 조회 전체를 막지 않는다.
    /// </summary>
    private async Task<RegisterRecap?> GetRecapAsync(string query, CancellationToken ct)
    {
        try { return (await GetItemsAsync("getBrRecapTitleInfo", query, ct)).Select(ParseRecap).FirstOrDefault(); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException or TaskCanceledException)
        { return null; }
    }

    private async Task<List<JsonElement>> GetItemsAsync(string op, string query, CancellationToken ct)
    {
        // 마이페이지의 Decoding 키(+ / = 포함)를 넣는 게 정상이고, Encoding 키(%2B 등)를 넣어도 되게 한다.
        var key = _key.Contains('%') ? _key : Uri.EscapeDataString(_key);
        var all = new List<JsonElement>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var url = $"{BaseUrl}{op}?serviceKey={key}&{query}&numOfRows={PageSize}&pageNo={page}&_type=json";
            using var res = await _http.GetAsync(url, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode && !text.TrimStart().StartsWith('{') && !text.Contains("returnReasonCode"))
                throw new InvalidOperationException(
                    $"건축물대장 서버 오류({(int)res.StatusCode}) — 공공데이터포털 쪽 문제입니다. 잠시 뒤 다시 하세요.");
            var (items, total) = ParsePage(text);
            all.AddRange(items);
            if (items.Count == 0 || all.Count >= total) break;
        }
        return all;
    }

    /// <summary>
    /// 응답 한 쪽을 읽는다. 오류는 사람이 고칠 수 있는 말로 바꿔 던진다 —
    /// 키 오류는 _type=json이어도 XML로 오는 경우가 있다.
    /// </summary>
    internal static (List<JsonElement> Items, int Total) ParsePage(string text)
    {
        var t = text.TrimStart();
        if (t.StartsWith('<'))
            throw new InvalidOperationException(ExplainError(
                Tag(t, "returnReasonCode") ?? Tag(t, "resultCode") ?? "",
                Tag(t, "returnAuthMsg") ?? Tag(t, "resultMsg") ?? Tag(t, "errMsg") ?? t[..Math.Min(200, t.Length)]));
        if (!t.StartsWith('{'))
            throw new InvalidOperationException(ExplainError("", t[..Math.Min(200, t.Length)]));

        using var doc = JsonDocument.Parse(t);
        var root = doc.RootElement.TryGetProperty("response", out var r) ? r : doc.RootElement;
        if (root.TryGetProperty("header", out var header))
        {
            var code = Str(header, "resultCode") ?? "00";
            if (code != "00" && code != "0") throw new InvalidOperationException(ExplainError(code, Str(header, "resultMsg") ?? ""));
        }

        var items = new List<JsonElement>();
        var total = 0;
        if (root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object)
        {
            total = (int)(Num(body, "totalCount") ?? 0);
            if (body.TryGetProperty("items", out var its) && its.ValueKind == JsonValueKind.Object &&
                its.TryGetProperty("item", out var item))
            {
                // 한 건이면 배열이 아니라 객체 하나로 온다.
                if (item.ValueKind == JsonValueKind.Array) items.AddRange(item.EnumerateArray().Select(e => e.Clone()));
                else if (item.ValueKind == JsonValueKind.Object) items.Add(item.Clone());
            }
        }
        return (items, total);
    }

    private static string ExplainError(string code, string msg)
    {
        var m = msg.ToUpperInvariant();
        if (m.Contains("SERVICE_KEY_IS_NOT_REGISTERED") || code is "30")
            return "건축물대장 키가 등록되지 않았습니다. 공공데이터포털에서 「건축HUB 건축물대장정보 서비스」를 활용신청했는지, " +
                   "마이페이지의 일반 인증키(Decoding)를 넣었는지 확인하세요. 신청 직후엔 1~2시간 뒤부터 됩니다.";
        if (m.Contains("SERVICE ACCESS DENIED") || code is "20")
            return "건축물대장 서비스 접근이 거부됐습니다 — 공공데이터포털에서 이 서비스를 활용신청(승인)했는지 확인하세요.";
        if (m.Contains("LIMITED_NUMBER") || code is "22")
            return "건축물대장 하루 조회 한도를 넘었습니다. 내일 다시 하거나 운영계정으로 신청하세요.";
        if (m.Contains("SERVICE_TIME_OUT") || code is "04" or "05")
            return "건축물대장 서버가 응답하지 않습니다. 잠시 뒤 다시 하세요.";
        return $"건축물대장 조회 오류 {code} {msg}".Trim();
    }

    private static string? Tag(string xml, string name)
    {
        var m = Regex.Match(xml, $"<{name}>(.*?)</{name}>", RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    // ── 항목 해석 ────────────────────────────────────────────────
    internal static RegisterRecap ParseRecap(JsonElement e) => new()
    {
        BuildingName = Str(e, "bldNm") ?? "",
        SiteArea = Num(e, "platArea"),
        BuildingArea = Num(e, "archArea"),
        CoverageRatio = Num(e, "bcRat"),
        GrossFloorArea = Num(e, "totArea"),
        FarFloorArea = Num(e, "vlRatEstmTotArea"),
        FloorAreaRatio = Num(e, "vlRat"),
        MainUse = Str(e, "mainPurpsCdNm") ?? "",
        BuildingCount = (int)(Num(e, "mainBldCnt") ?? 0),
        AnnexCount = (int)(Num(e, "atchBldCnt") ?? 0),
        ParkingTotal = (int)(Num(e, "totPkngCnt") ?? 0),
        ApprovalDate = Date(Str(e, "useAprDay")),
    };

    internal static RegisterBuilding ParseTitle(JsonElement e) => new()
    {
        DongName = Str(e, "dongNm") ?? "",
        BuildingName = Str(e, "bldNm") ?? "",
        IsAnnex = IsAnnexCode(e),
        RegisterKind = Str(e, "regstrKindCdNm") ?? "",
        MainUse = Str(e, "mainPurpsCdNm") ?? "",
        OtherUse = Str(e, "etcPurps") ?? "",
        Structure = Str(e, "strctCdNm") ?? Str(e, "etcStrct") ?? "",
        Roof = Str(e, "roofCdNm") ?? Str(e, "etcRoof") ?? "",
        SiteArea = Num(e, "platArea"),
        BuildingArea = Num(e, "archArea"),
        CoverageRatio = Num(e, "bcRat"),
        GrossFloorArea = Num(e, "totArea"),
        FarFloorArea = Num(e, "vlRatEstmTotArea"),
        FloorAreaRatio = Num(e, "vlRat"),
        Height = Num(e, "heit"),
        FloorsAbove = (int)(Num(e, "grndFlrCnt") ?? 0),
        FloorsBelow = (int)(Num(e, "ugrndFlrCnt") ?? 0),
        ParkingIndoor = (int)((Num(e, "indrAutoUtcnt") ?? 0) + (Num(e, "indrMechUtcnt") ?? 0)),
        ParkingOutdoor = (int)((Num(e, "oudrAutoUtcnt") ?? 0) + (Num(e, "oudrMechUtcnt") ?? 0)),
        ParkingMechanical = (int)((Num(e, "indrMechUtcnt") ?? 0) + (Num(e, "oudrMechUtcnt") ?? 0)),
        Elevators = (int)((Num(e, "rideUseElvtCnt") ?? 0) + (Num(e, "emgenUseElvtCnt") ?? 0)),
        Households = (int)(Num(e, "hhldCnt") ?? 0),
        Units = (int)(Num(e, "hoCnt") ?? 0),
        ApprovalDate = Date(Str(e, "useAprDay")),
        PermitDate = Date(Str(e, "pmsDay")),
    };

    internal static RegisterFloor ParseFloor(JsonElement e)
    {
        var gb = Str(e, "flrGbCdNm") ?? "";
        var below = gb.Contains("지하") || Str(e, "flrGbCd") == "10";
        var rooftop = gb.Contains("옥탑") || Str(e, "flrGbCd") == "30";
        var no = (int)(Num(e, "flrNo") ?? 0);
        var label = Str(e, "flrNoNm") is { Length: > 0 } nm ? nm : (below ? $"지하{no}층" : $"{no}층");
        var main = Str(e, "mainPurpsCdNm") ?? "";
        var etc = Str(e, "etcPurps") ?? "";
        return new RegisterFloor
        {
            DongName = Str(e, "dongNm") ?? "",
            IsAnnex = IsAnnexCode(e),
            Label = label,
            Below = below,
            Rooftop = rooftop,
            Number = no,
            // 층별 기타용도가 대장 "용도" 칸의 원문이다(예: 제1종근린생활시설(의원)). 주용도코드명은 "의원"뿐이다.
            Use = etc.Length > 0 ? etc : main,
            MainUse = main,
            Structure = Str(e, "strctCdNm") ?? "",
            Area = Num(e, "area") ?? 0,
            ExcludedFromGross = Str(e, "areaExctYn") == "1" || Str(e, "areaExctYn") == "Y",
        };
    }

    private static bool IsAnnexCode(JsonElement e) =>
        Str(e, "mainAtchGbCd") == "1" || (Str(e, "mainAtchGbCdNm") ?? "").Contains("부속");

    private static string? Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        var s = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static double? Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return p;
        return null;
    }

    /// <summary>"20150312" → "2015-03-12". 비었거나 형식이 다르면 원문 그대로.</summary>
    private static string Date(string? s) =>
        s is { Length: 8 } && s.All(char.IsDigit) ? $"{s[..4]}-{s[4..6]}-{s[6..]}" : s ?? "";
}

/// <summary>필지 하나의 건축물대장.</summary>
public sealed record BuildingRegister(string Pnu, RegisterRecap? Recap, List<RegisterBuilding> Buildings)
{
    public bool HasBuildings => Buildings.Count > 0;

    /// <summary>
    /// 필지 전체 기존 건축면적. 총괄표제부가 있으면 그 값(동 사이 중복 없이 정리된 합계),
    /// 없으면 동별 표제부의 합.
    /// </summary>
    public double? TotalBuildingArea =>
        Recap?.BuildingArea is > 0 ? Recap.BuildingArea : SumOrNull(Buildings.Select(b => b.BuildingArea));

    public double? TotalGrossFloorArea =>
        Recap?.GrossFloorArea is > 0 ? Recap.GrossFloorArea : SumOrNull(Buildings.Select(b => b.GrossFloorArea));

    private static double? SumOrNull(IEnumerable<double?> xs)
    {
        var v = xs.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return v.Count == 0 ? null : v.Sum();
    }
}

public sealed class RegisterRecap
{
    public string BuildingName { get; init; } = "";
    public double? SiteArea { get; init; }
    public double? BuildingArea { get; init; }
    public double? CoverageRatio { get; init; }
    public double? GrossFloorArea { get; init; }
    public double? FarFloorArea { get; init; }
    public double? FloorAreaRatio { get; init; }
    public string MainUse { get; init; } = "";
    public int BuildingCount { get; init; }
    public int AnnexCount { get; init; }
    public int ParkingTotal { get; init; }
    public string ApprovalDate { get; init; } = "";
}

/// <summary>표제부 한 동.</summary>
public sealed class RegisterBuilding
{
    public string DongName { get; init; } = "";
    public string BuildingName { get; init; } = "";
    public bool IsAnnex { get; init; }
    public string RegisterKind { get; init; } = "";   // 일반건축물 / 집합건축물
    public string MainUse { get; init; } = "";
    public string OtherUse { get; init; } = "";
    public string Structure { get; init; } = "";
    public string Roof { get; init; } = "";
    public double? SiteArea { get; init; }
    public double? BuildingArea { get; init; }
    public double? CoverageRatio { get; init; }
    public double? GrossFloorArea { get; init; }
    public double? FarFloorArea { get; init; }
    public double? FloorAreaRatio { get; init; }
    public double? Height { get; init; }
    public int FloorsAbove { get; init; }
    public int FloorsBelow { get; init; }
    public int ParkingIndoor { get; init; }
    public int ParkingOutdoor { get; init; }
    public int ParkingMechanical { get; init; }
    public int Elevators { get; init; }
    public int Households { get; init; }
    public int Units { get; init; }
    public string ApprovalDate { get; init; } = "";
    public string PermitDate { get; init; } = "";
    public List<RegisterFloor> Floors { get; } = new();

    /// <summary>화면·검토서에 쓰는 동 이름. 대장에 동명칭이 비어 있으면 주/부속으로 부른다.</summary>
    public string DisplayName =>
        DongName.Length > 0 ? DongName : IsAnnex ? "부속동" : BuildingName.Length > 0 ? BuildingName : "주동";
}

/// <summary>층별개요 한 행.</summary>
public sealed class RegisterFloor
{
    public string DongName { get; init; } = "";
    public bool IsAnnex { get; init; }
    public string Label { get; init; } = "";
    public bool Below { get; init; }
    public bool Rooftop { get; init; }
    public int Number { get; init; }
    public string Use { get; init; } = "";
    public string MainUse { get; init; } = "";
    public string Structure { get; init; } = "";
    public double Area { get; init; }
    public bool ExcludedFromGross { get; init; }

    /// <summary>지하 깊은 층 → 지상 → 옥탑 순. 면적표가 아래층부터 쌓이도록.</summary>
    public int SortKey => Below ? -Number : Rooftop ? 1000 + Number : Number;
}
