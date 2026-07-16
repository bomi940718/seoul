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

        /// <summary>프로젝트별 출력 폴더 예외. 비우면 공통 출력 루트 사용.</summary>
        [JsonProperty("outputDir")]
        public string OutputDir { get; set; }
    }
}
