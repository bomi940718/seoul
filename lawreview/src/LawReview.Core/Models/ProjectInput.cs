using System.Text.Json.Serialization;

namespace LawReview.Core.Models;

/// <summary>
/// 공사의 종류. 화면 상단 모드와 같고, 계산·표기가 여기에 따라 달라진다.
///
/// 신축이 아니면 설계개요·면적표가 **변경전 / 변경후** 두 벌이 된다.
/// 법정 검토는 변경후를 기준으로 하되, 주차처럼 증감분이 의미를 갖는 항목은 둘 다 산정한다.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProjectMode
{
    신축,
    리모델링,
    증축,
    용도변경,
}

/// <summary>
/// 법규검토 대상 프로젝트의 입력 정보. 검토서의 "설계개요" 블록에 대응한다.
/// </summary>
public sealed class ProjectInput
{
    /// <summary>공사 종류. 신축이 아니면 기존 건축물 값이 함께 들어온다.</summary>
    public ProjectMode Mode { get; set; } = ProjectMode.신축;

    public string ProjectName { get; set; } = "";      // 사업명
    public string Client { get; set; } = "";           // 건축주
    public string SiteAddress { get; set; } = "";      // 대지위치 (지번 주소)
    public string Province { get; set; } = "";         // 광역 지자체 (예: 대전광역시)
    public string City { get; set; } = "";             // 기초 지자체 (예: 유성구)
    public List<string> UseZones { get; set; } = new(); // 지역/지구 (예: 도시지역, 일반공업지역, 지구단위계획구역)
    public double SiteArea { get; set; }               // 대지면적 (㎡)
    public string PrimaryUse { get; set; } = "";       // 용도 (예: 공장)
    public string? AllowedUse { get; set; }            // 허용 용도 (지구단위계획 등)
    public string? DisallowedUse { get; set; }         // 불허 용도
    public string? UseBasis { get; set; }              // 허용/불허 용도의 근거 (예: 둔곡지구단위계획)

    public double PlannedBuildingArea { get; set; }    // 계획 건축면적 (㎡)
    public int PlannedFloorsAbove { get; set; }        // 지상 층수
    public int PlannedFloorsBelow { get; set; }        // 지하 층수
    public string? PlannedHeight { get; set; }         // 최고 높이 (예: "10 ~ 15 m")

    public List<BuildingArea> Buildings { get; set; } = new();

    public ZoningLimits Zoning { get; set; } = new();
    public ParkingRule Parking { get; set; } = new();

    /// <summary>
    /// 사용자가 직접 등록한 지구단위계획 결정도서(고시문·조서·지침 파일).
    /// 포털 자동조회가 되지 않는 지자체를 사용자가 파일로 보완하는 경로이며,
    /// **등록되어 있으면 포털 자동조회보다 우선한다** (해당 필지의 문서를 직접 지정한 것이므로).
    /// </summary>
    public List<DistrictPlanFile> DistrictPlanFiles { get; set; } = new();

    /// <summary>기존 건축면적 (㎡). 신축이 아닐 때만 쓴다.</summary>
    public double ExistingBuildingArea { get; set; }

    /// <summary>연면적 산입 대상 층 면적 합계 (연면적 제외 층 제외). 신축이 아니면 **변경후** 값이다.</summary>
    public double GrossFloorArea =>
        Buildings.Sum(b => b.Floors.Where(f => !f.ExcludeFromGrossArea).Sum(f => f.TotalArea));

    /// <summary>전체 바닥면적 합계 (연면적 제외 층 포함)</summary>
    public double TotalFloorArea =>
        Buildings.Sum(b => b.Floors.Sum(f => f.TotalArea));

    /// <summary>기존(변경전) 연면적. 신축이면 0이다.</summary>
    public double ExistingGrossFloorArea =>
        Buildings.Sum(b => b.Floors.Where(f => !f.ExcludeFromGrossArea).Sum(f => f.ExistingTotalArea));

    /// <summary>증감 연면적 (변경후 − 변경전). 증축이면 증축 면적이다.</summary>
    public double GrossFloorAreaDelta => GrossFloorArea - ExistingGrossFloorArea;

    /// <summary>신축이 아니고 기존 면적이 실제로 입력되어 있는지.</summary>
    public bool HasExisting => Mode != ProjectMode.신축 && ExistingGrossFloorArea > 0;
}

/// <summary>
/// 사용자가 직접 등록한 지구단위계획 문서 한 건.
/// 구역명·고시번호는 검토서 인용 표기에 쓰이며, 비워두면 파일명으로 대체한다.
/// </summary>
public sealed class DistrictPlanFile
{
    public string FilePath { get; set; } = "";
    public string? ZoneName { get; set; }      // 구역명 (예: 둔곡지구 지구단위계획구역)
    public string? NoticeNo { get; set; }      // 고시번호 (예: 대전광역시 고시 제2023-15호)
    public string? NoticeDate { get; set; }    // 고시일자 (예: 2023-01-20)

    public string DisplayName =>
        ZoneName is { Length: > 0 } z ? z
        : FilePath.Length > 0 ? Path.GetFileNameWithoutExtension(FilePath)
        : "(이름 없음)";
}

/// <summary>동(棟) 단위 면적표. 검토서의 "각 층별 면적표"에 대응.</summary>
public sealed class BuildingArea
{
    public string Name { get; set; } = "";             // 동 이름 (예: A(공장동))
    public List<FloorArea> Floors { get; set; } = new();

    public double GrossFloorArea => Floors.Where(f => !f.ExcludeFromGrossArea).Sum(f => f.TotalArea);
    public double TotalFloorArea => Floors.Sum(f => f.TotalArea);
}

public sealed class FloorArea
{
    public string FloorLabel { get; set; } = "";       // 층 (예: PIT, 1층, 지붕층)
    public string Use { get; set; } = "";              // 용도
    public double ExclusiveArea { get; set; }          // 전용 (㎡) — 신축이 아니면 변경후 값
    public double CommonArea { get; set; }             // 공용 (㎡) — 신축이 아니면 변경후 값
    public bool ExcludeFromGrossArea { get; set; }     // 연면적 제외 여부 (PIT 등)
    public string? Note { get; set; }                  // 비고

    /// <summary>기존(변경전) 전용면적. 신축이면 0.</summary>
    public double ExistingExclusiveArea { get; set; }
    /// <summary>기존(변경전) 공용면적. 신축이면 0.</summary>
    public double ExistingCommonArea { get; set; }

    public double TotalArea => ExclusiveArea + CommonArea;
    public double ExistingTotalArea => ExistingExclusiveArea + ExistingCommonArea;
    /// <summary>증감 면적 (변경후 − 변경전).</summary>
    public double DeltaArea => TotalArea - ExistingTotalArea;
}

/// <summary>
/// 해당 필지에 적용되는 법정 한도. 값의 출처(지구단위계획/조례/시행령)를 반드시 함께 기록한다.
/// 적용 우선순위: 지구단위계획 > 조례 > 시행령.
/// </summary>
public sealed class ZoningLimits
{
    public double? MaxCoverageRatio { get; set; }      // 법정 건폐율 상한 (%)
    public double? MaxFloorAreaRatio { get; set; }     // 법정 용적률 상한 (%)
    public int? MaxFloors { get; set; }                // 층수 제한
    public double? MaxHeight { get; set; }             // 높이 제한 (m)
    public double? LandscapeRatio { get; set; }        // 법정 조경면적 비율 (예: 0.05 = 대지면적의 5%)
    public string Source { get; set; } = "";           // 근거 (예: 국제과학비즈니스벨트 거점지구단위계획)
}

/// <summary>부설주차장 산정 기준. 값은 해당 지자체 주차장 조례에서 온다.</summary>
public sealed class ParkingRule
{
    public double AreaPerSpace { get; set; } = 200.0;  // 시설면적 N ㎡당 1대
    public double DisabledRatio { get; set; } = 0.03;  // 장애인전용 비율 (주차대수의 3% 이상)
    public int DisabledMinTotal { get; set; } = 10;    // 장애인전용 의무 발생 최소 주차대수
    public int ExpandedMinTotal { get; set; } = 50;    // 확장형 의무 발생 최소 주차대수
    public double ExpandedRatio { get; set; } = 0.03;
    public int EcoMinTotal { get; set; } = 50;         // 친환경(환경친화적 자동차) 의무 발생 최소 주차대수
    public double EcoRatio { get; set; } = 0.03;
    public string ParkingType { get; set; } = "";      // 주차형식 (예: 자주식) — 설계개요 표 첫 행
    public string Source { get; set; } = "";           // 근거 (예: 대전광역시 주차장 조례 제16조)
}
