using System;

namespace WorkReport.Core.Models
{
    /// <summary>일지 xlsx의 데이터 한 행.</summary>
    public class WorkRecord
    {
        /// <summary>DAY. 병합/빈 셀이면 위 행에서 상속된 값.</summary>
        public DateTime Date { get; set; }

        /// <summary>원본 셀에 날짜가 직접 기록되어 있었는지 (false = 상속).</summary>
        public bool DateInherited { get; set; }

        /// <summary>메일 접수/발송 (연락처 등).</summary>
        public string Mail { get; set; }

        /// <summary>Schedule — 표시 문자열 그대로 (예: "13.41").</summary>
        public string Schedule { get; set; }

        /// <summary>담당 소속기관 (Regiment).</summary>
        public string Regiment { get; set; }

        /// <summary>담당 외부담당자 (Outsider).</summary>
        public string Outsider { get; set; }

        /// <summary>PROJECT 이름 (H열).</summary>
        public string ProjectName { get; set; }

        /// <summary>PROJECT 넘버 원본값 (I열, 매칭 키).</summary>
        public string ProjectNumber { get; set; }

        /// <summary>본문. 셀 내 줄바꿈 포함.</summary>
        public string Descriptions { get; set; }

        /// <summary>여부 (O/X/△ 또는 빈 값).</summary>
        public string Status { get; set; }

        /// <summary>실행계획.</summary>
        public string Plan { get; set; }

        /// <summary>비고.</summary>
        public string Note { get; set; }

        /// <summary>작성자 표시명 (설정의 내/협업자 작성자명).</summary>
        public string Author { get; set; }

        /// <summary>소스 구분: 0 = 본인 파일, 1 = 협업자 파일. 동일 날짜 내 정렬에 사용.</summary>
        public int SourceIndex { get; set; }

        /// <summary>원본 시트 행 번호 (로그/정렬 안정성용).</summary>
        public int SourceRow { get; set; }
    }
}
