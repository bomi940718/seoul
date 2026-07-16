using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using WorkReport.Core.Models;

namespace WorkReport.Core.Parsing
{
    public class ParseResult
    {
        public List<WorkRecord> Records { get; } = new List<WorkRecord>();
        public List<string> Warnings { get; } = new List<string>();
        public string SheetName { get; set; }
        public int HeaderRow { get; set; }
        public ColumnMap Columns { get; set; }
        public string SourcePath { get; set; }
    }

    /// <summary>
    /// 일지 xlsx 파서. 열려 있는 파일도 읽을 수 있도록 FileShare.ReadWrite 스트림으로 연다.
    /// 소스 파일에는 절대 쓰지 않는다 (읽기 전용).
    /// </summary>
    public class JournalParser
    {
        /// <summary>헤더 탐색 범위 (행/열). 상단 제목+헤더 구조이므로 충분히 여유 있게.</summary>
        private const int HeaderSearchRows = 40;
        private const int HeaderSearchCols = 40;

        public ParseResult ParseFile(string path, string sheetName, string author, int sourceIndex)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var wb = new XLWorkbook(fs))
            {
                var result = Parse(wb, sheetName, author, sourceIndex);
                result.SourcePath = path;
                return result;
            }
        }

        public ParseResult Parse(XLWorkbook wb, string sheetName, string author, int sourceIndex)
        {
            var result = new ParseResult();

            var ws = ResolveSheet(wb, sheetName);
            if (ws == null)
                throw new InvalidOperationException(
                    $"시트 '{sheetName}' 을(를) 찾을 수 없습니다. 존재 시트: {string.Join(", ", wb.Worksheets.Select(s => s.Name))}");
            result.SheetName = ws.Name;

            int headerRow = FindHeaderRow(ws, out int dayCol);
            if (headerRow < 0)
                throw new InvalidOperationException($"시트 '{ws.Name}' 에서 헤더 행(\"DAY\" 셀)을 찾지 못했습니다.");
            result.HeaderRow = headerRow;

            var map = BuildColumnMap(ws, headerRow, dayCol, result.Warnings);
            result.Columns = map;

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
            DateTime? lastDate = null;

            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                string mail = Text(ws, r, map.Mail);
                string schedule = Text(ws, r, map.Schedule);
                string regiment = Text(ws, r, map.Regiment);
                string outsider = Text(ws, r, map.Outsider);
                string projName = Text(ws, r, map.ProjectName);
                string projNum = Text(ws, r, map.ProjectNumber);
                string desc = Text(ws, r, map.Descriptions);
                string status = Text(ws, r, map.Status);
                string plan = Text(ws, r, map.Plan);
                string note = Text(ws, r, map.Note);

                // 헤더 2단 구조의 서브헤더 행 (Regiment / Outsider) 건너뜀
                if (IsSubHeaderRow(regiment, outsider, desc, projNum)) continue;

                DateTime? date = ReadDate(ws.Cell(r, map.Day), result.Warnings, r);

                bool hasContent = new[] { mail, schedule, regiment, outsider, projName, projNum, desc, status, plan, note }
                    .Any(s => !string.IsNullOrWhiteSpace(s));

                // 빈 행, DAY만 있고 내용 없는 행은 건너뜀
                if (!hasContent) { if (date.HasValue) lastDate = date; continue; }

                bool inherited = false;
                if (!date.HasValue)
                {
                    if (lastDate.HasValue) { date = lastDate; inherited = true; }
                    else
                    {
                        result.Warnings.Add($"{r}행: 날짜가 없고 상속할 이전 날짜도 없어 건너뜀");
                        continue;
                    }
                }
                lastDate = date;

                result.Records.Add(new WorkRecord
                {
                    Date = date.Value,
                    DateInherited = inherited,
                    Mail = mail,
                    Schedule = schedule,
                    Regiment = regiment,
                    Outsider = outsider,
                    ProjectName = projName,
                    ProjectNumber = projNum,
                    Descriptions = desc,
                    Status = status,
                    Plan = plan,
                    Note = note,
                    Author = author,
                    SourceIndex = sourceIndex,
                    SourceRow = r,
                });
            }

            return result;
        }

        /// <summary>시트명 해석: 정확히 일치 → 대괄호 유무 변형 → 대소문자 무시.</summary>
        private static IXLWorksheet ResolveSheet(XLWorkbook wb, string sheetName)
        {
            string want = (sheetName ?? "").Trim();
            string wantBare = want.Trim('[', ']');

            foreach (var ws in wb.Worksheets)
            {
                string name = ws.Name.Trim();
                if (string.Equals(name, want, StringComparison.OrdinalIgnoreCase)) return ws;
                if (string.Equals(name.Trim('[', ']'), wantBare, StringComparison.OrdinalIgnoreCase)) return ws;
            }
            return null;
        }

        private static int FindHeaderRow(IXLWorksheet ws, out int dayCol)
        {
            int maxRow = Math.Min(HeaderSearchRows, ws.LastRowUsed()?.RowNumber() ?? 0);
            int maxCol = Math.Min(HeaderSearchCols, ws.LastColumnUsed()?.ColumnNumber() ?? 0);
            for (int r = 1; r <= maxRow; r++)
                for (int c = 1; c <= maxCol; c++)
                {
                    if (string.Equals(CellText(ws.Cell(r, c)).Trim(), "DAY", StringComparison.OrdinalIgnoreCase))
                    {
                        dayCol = c;
                        return r;
                    }
                }
            dayCol = -1;
            return -1;
        }

        /// <summary>
        /// DAY 앵커 기준 기본 오프셋으로 시작하고, 헤더 행/서브헤더 행의 텍스트로 보정한다.
        /// (실측 파일과 지시서 표의 열 배치가 한 칸 어긋났던 이력이 있어 텍스트 기반 보정을 우선한다.)
        /// </summary>
        private static ColumnMap BuildColumnMap(IXLWorksheet ws, int headerRow, int dayCol, List<string> warnings)
        {
            var map = ColumnMap.FromDayAnchor(dayCol);
            int maxCol = Math.Min(HeaderSearchCols, ws.LastColumnUsed()?.ColumnNumber() ?? dayCol + 10);

            for (int c = 1; c <= maxCol; c++)
            {
                string h = CellText(ws.Cell(headerRow, c)).Trim();
                if (h.Length == 0) continue;
                string hu = h.ToUpperInvariant().Replace("\n", " ");

                if (hu.Contains("메일")) map.Mail = c;
                else if (hu.Contains("SCHEDULE")) map.Schedule = c;
                else if (hu.Contains("PERSON IN CHARGE") || hu.Contains("담당")) { map.Regiment = c; map.Outsider = c + 1; }
                else if (hu == "PROJECT" || hu.StartsWith("PROJECT")) { map.ProjectName = c; map.ProjectNumber = c + 1; }
                else if (hu.Contains("DESCRIPTION")) map.Descriptions = c;
                else if (hu.Contains("여부")) map.Status = c;
                else if (hu.Contains("실행")) map.Plan = c;
                else if (hu.Contains("비고")) map.Note = c;
            }

            // 서브헤더 행(Regiment/Outsider)이 있으면 담당 열을 그 위치로 확정
            int subRow = headerRow + 1;
            for (int c = 1; c <= maxCol; c++)
            {
                string s = CellText(ws.Cell(subRow, c)).Trim().ToUpperInvariant();
                if (s == "REGIMENT") map.Regiment = c;
                else if (s == "OUTSIDER") map.Outsider = c;
            }

            var defaults = ColumnMap.FromDayAnchor(dayCol);
            if (map.ToString() != defaults.ToString())
                warnings.Add($"열 배치가 DAY 기준 기본 오프셋과 달라 헤더 텍스트 기준으로 보정됨: {map}");

            return map;
        }

        private static bool IsSubHeaderRow(string regiment, string outsider, string desc, string projNum)
            => string.Equals(regiment, "Regiment", StringComparison.OrdinalIgnoreCase)
               && string.Equals(outsider, "Outsider", StringComparison.OrdinalIgnoreCase)
               && string.IsNullOrWhiteSpace(desc) && string.IsNullOrWhiteSpace(projNum);

        private static DateTime? ReadDate(IXLCell cell, List<string> warnings, int row)
        {
            var v = cell.Value;
            if (v.IsDateTime) return v.GetDateTime();
            if (v.IsNumber)
            {
                double n = v.GetNumber();
                // 엑셀 날짜 시리얼 (1990년대~2100년대 범위만 인정)
                if (n >= 32874 && n <= 80000) return DateTime.FromOADate(n);
                warnings.Add($"{row}행: DAY 열 숫자값 {n} 은 날짜 시리얼 범위를 벗어나 무시함");
                return null;
            }
            if (v.IsText)
            {
                string t = v.GetText().Trim();
                if (t.Length == 0) return null;
                if (DateTime.TryParse(t, out var dt)) return dt;
                warnings.Add($"{row}행: DAY 열 텍스트 '{t}' 를 날짜로 해석하지 못함");
            }
            return null;
        }

        /// <summary>표시 문자열 기준 읽기 (숫자 서식 유지, 셀 내 줄바꿈 유지).</summary>
        private static string Text(IXLWorksheet ws, int row, int col)
            => col <= 0 ? string.Empty : CellText(ws.Cell(row, col));

        private static string CellText(IXLCell cell)
        {
            var v = cell.Value;
            if (v.IsBlank) return string.Empty;
            if (v.IsText) return v.GetText();
            return cell.GetFormattedString();
        }
    }
}
