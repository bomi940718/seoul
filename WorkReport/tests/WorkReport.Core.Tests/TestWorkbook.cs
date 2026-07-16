using ClosedXML.Excel;

namespace WorkReport.Core.Tests
{
    /// <summary>
    /// 실제 일지 파일과 같은 구조의 합성 워크북 생성 (개인정보 없는 테스트 픽스처).
    /// 실측 구조: 2행 제목, 4행 헤더(DAY=C4, PROJECT는 H4:I5 병합), 5행 서브헤더(Regiment/Outsider), 7행부터 데이터.
    /// </summary>
    public static class TestWorkbook
    {
        public static XLWorkbook Create(string sheetName = "2026")
        {
            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet(sheetName);

            ws.Cell(2, 3).Value = "테스트 일지";

            ws.Cell(4, 3).Value = "DAY";
            ws.Cell(4, 4).Value = "메일 접수/발송";
            ws.Cell(4, 5).Value = "Schedule";
            ws.Cell(4, 6).Value = "The person in charge";
            ws.Range(4, 6, 4, 7).Merge();
            ws.Cell(5, 6).Value = "Regiment";
            ws.Cell(5, 7).Value = "Outsider";
            ws.Cell(4, 8).Value = "PROJECT";
            ws.Range(4, 8, 5, 9).Merge();
            ws.Cell(4, 10).Value = "Descriptions";
            ws.Cell(4, 11).Value = "여부";
            ws.Cell(4, 12).Value = "실행\n계획";
            ws.Cell(4, 13).Value = "비고";

            return wb;
        }

        public static void AddRow(IXLWorksheet ws, int row, object day, string projName, string projNum,
            string desc, string status = "O", string regiment = "", string outsider = "",
            string mail = "", string plan = "", string note = "")
        {
            if (day != null)
            {
                var c = ws.Cell(row, 3);
                if (day is double serial)
                {
                    // 실제 파일처럼 날짜 시리얼 숫자 + 날짜 서식으로 저장
                    c.Value = serial;
                    c.Style.NumberFormat.Format = "yyyy\\.mm\\.dd(aaa)";
                }
                else c.Value = XLCellValue.FromObject(day);
            }
            ws.Cell(row, 4).Value = mail;
            ws.Cell(row, 6).Value = regiment;
            ws.Cell(row, 7).Value = outsider;
            ws.Cell(row, 8).Value = projName;
            ws.Cell(row, 9).Value = projNum;
            ws.Cell(row, 10).Value = desc;
            ws.Cell(row, 11).Value = status;
            ws.Cell(row, 12).Value = plan;
            ws.Cell(row, 13).Value = note;
        }
    }
}
