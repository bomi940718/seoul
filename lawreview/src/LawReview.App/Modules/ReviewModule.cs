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

    public Control CreateControl()
    {
        var root = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 420 };

        // 상단: 입력 (좌: 프로젝트 정보, 우: 면적표)
        var top = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 480 };
        top.Panel1.Controls.Add(BuildInputPanel());
        top.Panel2.Controls.Add(BuildAreaPanel());
        root.Panel1.Controls.Add(top);

        // 하단: 실행 버튼 + 로그
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _runButton.Dock = DockStyle.Fill;
        _runButton.Click += OnRunClicked;
        bottom.Controls.Add(_runButton, 0, 0);
        bottom.Controls.Add(_log, 0, 1);
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
        AddRow("지역/지구 (쉼표 구분)", _useZones);
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

    private async void OnRunClicked(object? sender, EventArgs e)
    {
        var settings = AppSettings.Load();
        if (settings.MolegApiKey.Length == 0)
        {
            MessageBox.Show("설정 탭에서 법제처 Open API 키를 먼저 입력하세요. (open.law.go.kr 무료 발급)",
                "설정 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (settings.ClaudeApiKey.Length == 0)
        {
            MessageBox.Show("설정 탭에서 Claude API 키를 먼저 입력하세요.",
                "설정 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            var judge = new ClaudeJudgmentProvider(Http, settings.ClaudeApiKey, settings.ClaudeModel);
            var progress = new Progress<string>(msg => _log.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}\r\n"));

            var engine = new ReviewEngine(moleg, judge, progress);
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
