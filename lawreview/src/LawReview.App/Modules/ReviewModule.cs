using LawReview.Core;
using LawReview.Core.Ai;
using LawReview.Core.LawApi;
using LawReview.Core.Models;
using LawReview.Core.Report;
using LawReview.Core.Review;

namespace LawReview.App.Modules;

/// <summary>
/// 법규검토 모듈: 프로젝트 정보 입력 → 면적표 입력 → 검토 실행 → 검토서.docx 저장.
/// </summary>
public sealed class ReviewModule : IAppModule
{
    public string Title => "법규검토";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    // 프로젝트 정보 입력란
    private readonly TextBox _projectName = new();
    private readonly TextBox _client = new();
    private readonly TextBox _address = new();
    private readonly TextBox _province = new();
    private readonly TextBox _city = new();
    private readonly TextBox _useZones = new();
    private readonly TextBox _siteArea = new();
    private readonly TextBox _primaryUse = new();
    private readonly TextBox _buildingArea = new();
    private readonly NumericUpDown _floorsAbove = new() { Minimum = 0, Maximum = 200 };
    private readonly NumericUpDown _floorsBelow = new() { Minimum = 0, Maximum = 20 };

    // 법정 한도 (지구단위계획/조례 값)
    private readonly TextBox _maxCoverage = new();
    private readonly TextBox _maxFar = new();
    private readonly TextBox _maxFloors = new();
    private readonly TextBox _zoningSource = new();
    private readonly TextBox _parkingAreaPerSpace = new() { Text = "200" };
    private readonly TextBox _parkingSource = new();

    // 면적표 그리드 (엑셀에서 복사-붙여넣기 가능)
    private readonly DataGridView _areaGrid = new()
    {
        AllowUserToAddRows = true,
        Dock = DockStyle.Fill,
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
    };

    private readonly TextBox _log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
    };
    private readonly Button _runButton = new() { Text = "검토 실행 → 검토서 저장", Height = 40 };
    private readonly Button _sampleButton = new() { Text = "예시 입력 (둔곡 공장)", Height = 40, Width = 160 };

    public Control CreateControl()
    {
        var root = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 420 };

        // 상단: 입력 (좌: 프로젝트 정보, 우: 면적표)
        var top = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 480 };
        top.Panel1.Controls.Add(BuildInputPanel());
        top.Panel2.Controls.Add(BuildAreaPanel());
        root.Panel1.Controls.Add(top);

        // 하단: 실행 버튼 + 로그
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2 };
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _sampleButton.Dock = DockStyle.Fill;
        _sampleButton.Click += (_, _) => FillSample();
        _runButton.Dock = DockStyle.Fill;
        _runButton.Click += OnRunClicked;
        bottom.Controls.Add(_sampleButton, 0, 0);
        bottom.Controls.Add(_runButton, 1, 0);
        bottom.Controls.Add(_log, 0, 1);
        bottom.SetColumnSpan(_log, 2);
        root.Panel2.Controls.Add(bottom);

        return root;
    }

    private Control BuildInputPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(8),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control input)
        {
            panel.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, panel.RowCount);
            input.Dock = DockStyle.Fill;
            panel.Controls.Add(input, 1, panel.RowCount);
            panel.RowCount++;
        }

        AddRow("사업명", _projectName);
        AddRow("건축주", _client);
        AddRow("대지위치 (지번)", _address);
        AddRow("광역 지자체", _province);       // 예: 대전광역시
        AddRow("기초 지자체", _city);           // 예: 유성구

        // 지역/지구: 직접 입력 + VWorld 토지이음 색인으로 자동 채움 (이름 색인만 — 원칙 2)
        var zoneRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        zoneRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        zoneRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        _useZones.Dock = DockStyle.Fill;
        var zoneLookup = new Button { Text = "자동조회", Dock = DockStyle.Fill, Margin = new Padding(2, 0, 0, 0) };
        zoneLookup.Click += OnZoneLookupClicked;
        zoneRow.Controls.Add(_useZones, 0, 0);
        zoneRow.Controls.Add(zoneLookup, 1, 0);
        AddRow("지역/지구 (쉼표 구분)", zoneRow);
        AddRow("대지면적 (㎡)", _siteArea);
        AddRow("용도", _primaryUse);
        AddRow("건축면적 (㎡)", _buildingArea);
        AddRow("지상 층수", _floorsAbove);
        AddRow("지하 층수", _floorsBelow);
        AddRow("법정 건폐율 (%)", _maxCoverage);
        AddRow("법정 용적률 (%)", _maxFar);
        AddRow("층수 제한", _maxFloors);
        AddRow("한도 근거 (지구단위계획/조례)", _zoningSource);
        AddRow("주차: 시설면적 N㎡당 1대", _parkingAreaPerSpace);
        AddRow("주차 기준 근거 (조례)", _parkingSource);
        return panel;
    }

    private Control BuildAreaPanel()
    {
        _areaGrid.Columns.Add("bldg", "동");
        _areaGrid.Columns.Add("floor", "층");
        _areaGrid.Columns.Add("use", "용도");
        _areaGrid.Columns.Add("excl", "전용(㎡)");
        _areaGrid.Columns.Add("common", "공용(㎡)");
        var exclude = new DataGridViewCheckBoxColumn { Name = "skipGfa", HeaderText = "연면적 제외" };
        _areaGrid.Columns.Add(exclude);

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = "면적표 — 엑셀에서 행을 복사해 붙여넣을 수 있습니다 (동/층/용도/전용/공용 순)",
            Dock = DockStyle.Fill,
        }, 0, 0);
        panel.Controls.Add(_areaGrid, 0, 1);

        _areaGrid.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.V) PasteIntoGrid();
        };
        return panel;
    }

    private void PasteIntoGrid()
    {
        if (!Clipboard.ContainsText()) return;
        foreach (var line in Clipboard.GetText().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cells = line.TrimEnd('\r').Split('\t');
            var idx = _areaGrid.Rows.Add();
            for (var c = 0; c < Math.Min(cells.Length, 5); c++)
                _areaGrid.Rows[idx].Cells[c].Value = cells[c];
        }
    }

    /// <summary>대지위치 주소로 용도지역·지구를 조회해 지역/지구 입력란을 채운다 (VWorld 색인).</summary>
    private async void OnZoneLookupClicked(object? sender, EventArgs e)
    {
        var settings = AppSettings.Load();
        if (settings.VworldApiKey.Length == 0)
        {
            MessageBox.Show("설정 탭에서 VWorld 키를 먼저 입력하세요. (www.vworld.kr 무료 발급)\n" +
                            "키 없이 쓰려면 지역/지구를 직접 입력하면 됩니다.",
                "설정 필요", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_address.Text.Trim().Length == 0)
        {
            MessageBox.Show("대지위치(지번 주소)를 먼저 입력하세요.", "입력 확인",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var vworld = new LawReview.Core.LandUse.VworldClient(Http, settings.VworldApiKey, settings.VworldDomain);
            var index = await vworld.GetLandUseIndexAsync(_address.Text.Trim());
            if (index is null || index.Zones.Count == 0)
            {
                _log.AppendText($"{DateTime.Now:HH:mm:ss}  용도지역 조회 결과 없음 — 지번 주소인지 확인하세요.\r\n");
                return;
            }
            _useZones.Text = string.Join(", ", index.Zones);
            _log.AppendText($"{DateTime.Now:HH:mm:ss}  용도지역 자동조회 (PNU {index.Pnu}): {_useZones.Text}\r\n");
        }
        catch (Exception ex)
        {
            _log.AppendText($"{DateTime.Now:HH:mm:ss}  용도지역 조회 실패: {ex.Message}\r\n");
            MessageBox.Show($"용도지역 조회에 실패했습니다: {ex.Message}", "조회 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void OnRunClicked(object? sender, EventArgs e)
    {
        var settings = AppSettings.Load();
        if (settings.MolegApiKey.Length == 0)
        {
            MessageBox.Show("설정 탭에서 법제처 Open API 키를 먼저 입력하세요. (open.law.go.kr 무료 발급)",
                "설정 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (settings.ClaudeApiKey.Length == 0
            && MessageBox.Show(
                "Claude API 키가 없습니다. AI 판정 없이 진행할까요?\n" +
                "(조문 인용·건폐율/용적률/주차 계산·검토서 생성은 그대로 동작하고,\n" +
                " AI 판정 항목은 전부 \"확인필요\"로 표시됩니다.)",
                "AI 판정 생략", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        ProjectInput project;
        try
        {
            project = CollectInput();
        }
        catch (FormatException ex)
        {
            MessageBox.Show($"입력값 오류: {ex.Message}", "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Word 문서 (*.docx)|*.docx",
            FileName = $"{project.ProjectName}_법규검토서_{DateTime.Now:yyyyMMdd}.docx",
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        _runButton.Enabled = false;
        _log.Clear();
        try
        {
            var checklistPath = Path.Combine(AppContext.BaseDirectory, "checklists", "standard.json");
            var checklist = ChecklistLoader.Load(checklistPath);

            var moleg = new MolegClient(Http, settings.MolegApiKey);
            IJudgmentProvider judge = settings.ClaudeApiKey.Length > 0
                ? new ClaudeJudgmentProvider(Http, settings.ClaudeApiKey, settings.ClaudeModel)
                : new OfflineJudgmentProvider();
            var progress = new Progress<string>(msg => _log.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}\r\n"));

            // 지구단위계획 조회 — 구현된 지자체(서울)만 제공자가 붙고, 그 외는 기존 수동 안내 유지.
            var districtPlan = LawReview.Core.Municipal.DistrictPlanProviders.For(project.Province, Http);
            var engine = new ReviewEngine(moleg, judge, progress, districtPlan);
            var result = await engine.RunAsync(project, checklist);

            new DocxReportBuilder().Build(result, dialog.FileName);
            _log.AppendText($"검토서 저장 완료: {dialog.FileName}\r\n");

            if (MessageBox.Show("검토서를 저장했습니다. 지금 열까요?", "완료",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName)
                {
                    UseShellExecute = true,
                });
        }
        catch (Exception ex)
        {
            _log.AppendText($"오류: {ex.Message}\r\n");
            MessageBox.Show(ex.Message, "검토 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _runButton.Enabled = true;
        }
    }

    /// <summary>테스트 검증 기준인 실무 검토서(대전 둔곡, 2023) 입력을 채운다 — 결과를 바로 눈으로 확인하는 용도.</summary>
    private void FillSample()
    {
        _projectName.Text = "세이퍼존 둔곡 공장";
        _client.Text = "(주)세이퍼존";
        _address.Text = "대전광역시 유성구 둔곡동 407-5";
        _province.Text = "대전광역시";
        _city.Text = "유성구";
        _useZones.Text = "도시지역, 일반공업지역, 지구단위계획구역(국제과학비즈니스벨트 거점지구)";
        _siteArea.Text = "6030.10";
        _primaryUse.Text = "공장";
        _buildingArea.Text = "1453.22";
        _floorsAbove.Value = 2;
        _floorsBelow.Value = 0;
        _maxCoverage.Text = "70";
        _maxFar.Text = "350";
        _maxFloors.Text = "7";
        _zoningSource.Text = "국제과학비즈니스벨트 거점지구단위계획";
        _parkingAreaPerSpace.Text = "200";
        _parkingSource.Text = "대전광역시 주차장 조례 제16조";

        _areaGrid.Rows.Clear();
        _areaGrid.Rows.Add("A(공장동)", "PIT", "공장", "", "110.06", true);
        _areaGrid.Rows.Add("A(공장동)", "1층", "공장", "926.81", "241.07", false);
        _areaGrid.Rows.Add("A(공장동)", "2층", "공장", "1110.72", "187.03", false);
        _areaGrid.Rows.Add("B(경비동)", "1층", "경비실", "18.80", "", false);
    }

    private ProjectInput CollectInput()
    {
        var project = new ProjectInput
        {
            ProjectName = _projectName.Text.Trim(),
            Client = _client.Text.Trim(),
            SiteAddress = _address.Text.Trim(),
            Province = _province.Text.Trim(),
            City = _city.Text.Trim(),
            UseZones = _useZones.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            SiteArea = ParseArea(_siteArea.Text, "대지면적"),
            PrimaryUse = _primaryUse.Text.Trim(),
            PlannedBuildingArea = ParseArea(_buildingArea.Text, "건축면적"),
            PlannedFloorsAbove = (int)_floorsAbove.Value,
            PlannedFloorsBelow = (int)_floorsBelow.Value,
            Zoning = new ZoningLimits
            {
                MaxCoverageRatio = ParseOptional(_maxCoverage.Text),
                MaxFloorAreaRatio = ParseOptional(_maxFar.Text),
                MaxFloors = ParseOptional(_maxFloors.Text) is double mf ? (int)mf : null,
                Source = _zoningSource.Text.Trim(),
            },
            Parking = new ParkingRule
            {
                AreaPerSpace = ParseArea(_parkingAreaPerSpace.Text, "주차 기준 면적"),
                Source = _parkingSource.Text.Trim(),
            },
        };

        foreach (DataGridViewRow row in _areaGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var bldgName = row.Cells["bldg"].Value?.ToString()?.Trim() ?? "";
            if (bldgName.Length == 0) continue;

            var building = project.Buildings.FirstOrDefault(b => b.Name == bldgName);
            if (building is null)
            {
                building = new BuildingArea { Name = bldgName };
                project.Buildings.Add(building);
            }
            building.Floors.Add(new FloorArea
            {
                FloorLabel = row.Cells["floor"].Value?.ToString()?.Trim() ?? "",
                Use = row.Cells["use"].Value?.ToString()?.Trim() ?? "",
                ExclusiveArea = ParseOptional(row.Cells["excl"].Value?.ToString() ?? "") ?? 0,
                CommonArea = ParseOptional(row.Cells["common"].Value?.ToString() ?? "") ?? 0,
                ExcludeFromGrossArea = row.Cells["skipGfa"].Value is true,
            });
        }
        return project;
    }

    private static double ParseArea(string text, string fieldName) =>
        double.TryParse(text.Replace(",", "").Trim(), out var v)
            ? v
            : throw new FormatException($"{fieldName}에 숫자를 입력하세요. (입력값: \"{text}\")");

    private static double? ParseOptional(string text) =>
        double.TryParse(text.Replace(",", "").Trim(), out var v) ? v : null;
}
