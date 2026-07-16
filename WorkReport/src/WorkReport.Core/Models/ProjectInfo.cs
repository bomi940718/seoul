using Newtonsoft.Json;

namespace WorkReport.Core.Models
{
    /// <summary>projects.json 에 등록되는 프로젝트 1건.</summary>
    public class ProjectInfo
    {
        /// <summary>프로젝트 넘버(구분코드) — I열 매칭 키. 예: BRANDING-1.</summary>
        [JsonProperty("number")]
        public string Number { get; set; }

        /// <summary>프로젝트명. 예: 스포츠 브라.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("active")]
        public bool Active { get; set; } = true;

        /// <summary>프로젝트 성격 그룹 (대시보드 섹션/필터용). 예: 교육, 관리, 브랜딩. 비우면 "미분류".</summary>
        [JsonProperty("group")]
        public string Group { get; set; } = "";

        /// <summary>프로젝트 상태: 계획 / 진행 / 완료. HTML 대시보드에서만 사용 (완료는 기본 숨김).</summary>
        [JsonProperty("status")]
        public string Status { get; set; } = StatusActive;

        public const string StatusPlanned = "계획";
        public const string StatusActive = "진행";
        public const string StatusDone = "완료";

        [JsonIgnore]
        public string EffectiveGroup => string.IsNullOrWhiteSpace(Group) ? "미분류" : Group.Trim();

        [JsonIgnore]
        public string EffectiveStatus
        {
            get
            {
                var s = (Status ?? "").Trim();
                return s == StatusPlanned || s == StatusDone ? s : StatusActive;
            }
        }

        /// <summary>프로젝트별 출력 폴더 예외. 비우면 공통 출력 루트 사용.</summary>
        [JsonProperty("outputDir")]
        public string OutputDir { get; set; }
    }
}
