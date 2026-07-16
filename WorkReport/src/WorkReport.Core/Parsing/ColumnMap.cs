namespace WorkReport.Core.Parsing
{
    /// <summary>
    /// 시트 내 열 위치 (1-based). 하드코딩 금지 원칙에 따라
    /// 헤더 행의 "DAY" 셀을 앵커로 탐지하고, 헤더 텍스트로 보정한다.
    /// </summary>
    public class ColumnMap
    {
        public int Day { get; set; }
        public int Mail { get; set; }
        public int Schedule { get; set; }
        public int Regiment { get; set; }
        public int Outsider { get; set; }
        public int ProjectName { get; set; }
        public int ProjectNumber { get; set; }
        public int Descriptions { get; set; }
        public int Status { get; set; }
        public int Plan { get; set; }
        public int Note { get; set; }

        /// <summary>DAY 앵커 기준 기본 오프셋 배치 (지시서 표와 실측 파일 모두 이 간격).</summary>
        public static ColumnMap FromDayAnchor(int dayCol) => new ColumnMap
        {
            Day = dayCol,
            Mail = dayCol + 1,
            Schedule = dayCol + 2,
            Regiment = dayCol + 3,
            Outsider = dayCol + 4,
            ProjectName = dayCol + 5,
            ProjectNumber = dayCol + 6,
            Descriptions = dayCol + 7,
            Status = dayCol + 8,
            Plan = dayCol + 9,
            Note = dayCol + 10,
        };

        public override string ToString()
            => $"DAY={Day} 메일={Mail} Schedule={Schedule} Regiment={Regiment} Outsider={Outsider} " +
               $"PROJECT명={ProjectName} PROJECT넘버={ProjectNumber} Descriptions={Descriptions} 여부={Status} 실행계획={Plan} 비고={Note}";
    }
}
