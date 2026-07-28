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

    // 지구단위계획 결정도서 직접 등록 (포털 자동조회가 안 되는 지자체용 — 등록 시 자동조회보다 우선)
    private readonly ListBox _districtPlanList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly List<DistrictPlanFile> _districtPlanFiles = new();

    private readonly TextBox _log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
    };
    private readonly Button _runButton = new() { Text = "검토 실행 → 검토서 저장", Height = 40 };
    private readonly Button _sampleButton = new() { Text = "예시 입력 (둔곡 공장)", Height = 40, Width = 160 };

    public Control CreateControl()
    {
        var root = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 420 };

        // 상단: 입력 (좌: 프로젝트 정보, 우: 면적표 + 지구단위계획 등록)
        var top = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 480 };
        top.Panel1.Controls.Add(BuildInputPanel());

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        right.Panel1.Controls.Add(BuildAreaPanel());
        right.Panel2.Controls.Add(BuildDistrictPlanPanel());
        top.Panel2.Controls.Add(right);
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

    /// <summary>
    /// 지구단위계획 결정도서 직접 등록 패널.
    /// 서울처럼 포털 수집기가 있는 지자체는 자동조회되지만, 그 외 지역은 사용자가 파일을 등록한다.
    /// 등록된 파일이 있으면 자동조회보다 우선한다(중복 인용 방지).
    /// </summary>
    private Control BuildDistrictPlanPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(4) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        panel.Controls.Add(new Label
        {
            Text = "지구단위계획 결정도서 (직접 등록 시 포털 자동조회보다 우선 적용)",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        panel.Controls.Add(_districtPlanList, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var add = new Button { Text = "파일 등록...", Width = 110 };
        var remove = new Button { Text = "선택 삭제", Width = 90 };
        add.Click += (_, _) => AddDistrictPlanFiles();
        remove.Click += (_, _) => RemoveSelectedDistrictPlan();
        buttons.Controls.Add(add);
        buttons.Controls.Add(remove);
        panel.Controls.Add(buttons, 0, 2);
        return panel;
    }

    private void AddDistrictPlanFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "지구단위계획 결정도서 선택 (고시문·조서·지침)",
            Filter = "문서 (*.pdf;*.hwp;*.hwpx;*.docx;*.zip)|*.pdf;*.hwp;*.hwpx;*.docx;*.zip|모든 파일 (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        foreach (var path in dialog.FileNames)
        {
            if (_districtPlanFiles.Any(f => string.Equals(f.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            // 구역명·고시번호는 검토서 표기에 쓰이며, 비워두면 파일명으로 대체된다.
            var zone = Prompt($"구역명 (선택)\n{Path.GetFileName(path)}", "지구단위계획 등록");
            var notice = Prompt("고시번호 (선택)\n예: 안양시 고시 제2024-15호", "지구단위계획 등록");
            _districtPlanFiles.Add(new DistrictPlanFile
            {
                FilePath = path,
                ZoneName = string.IsNullOrWhiteSpace(zone) ? null : zone.Trim(),
                NoticeNo = string.IsNullOrWhiteSpace(notice) ? null : notice.Trim(),
            });
        }
        RefreshDistrictPlanList();
    }

    private void RemoveSelectedDistrictPlan()
    {
        var idx = _districtPlanList.SelectedIndex;
        if (idx < 0 || idx >= _districtPlanFiles.Count) return;
        _districtPlanFiles.RemoveAt(idx);
        RefreshDistrictPlanList();
    }

    private void RefreshDistrictPlanList()
    {
        _districtPlanList.Items.Clear();
        foreach (var f in _districtPlanFiles)
            _districtPlanList.Items.Add(
                $"{f.DisplayName}{(f.NoticeNo is { Length: > 0 } n ? $"  [{n}]" : "")}  —  {Path.GetFileName(f.FilePath)}");
    }

    /// <summary>간단한 한 줄 입력 대화상자 (디자이너를 쓰지 않는 구조라 코드로 구성).</summary>
    private static string? Prompt(string message, string title)
    {
        using var form = new Form
        {
            Text = title, Width = 460, Height = 190,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false,
        };
        var label = new Label { Text = message, Dock = DockStyle.Top, Height = 56, Padding = new Padding(10, 10, 10, 0) };
        var input = new TextBox { Dock = DockStyle.Top, Margin = new Padding(10) };
        var ok = new Button { Text = "확인", DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 90 };
        var skip = new Button { Text = "건너뛰기", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 90 };
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(10) };
        bar.Controls.Add(ok);
        bar.Controls.Add(skip);
        form.Controls.Add(input);
        form.Controls.Add(label);
        form.Controls.Add(bar);
        form.AcceptButton = ok;
        form.CancelButton = skip;
        return form.ShowDialog() == DialogResult.OK ? input.Text : null;
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
            if (index is null)
            {
                _log.AppendText($"{DateTime.Now:HH:mm:ss}  조회 결과 없음 — 지번 주소인지 확인하세요 (도로명 주소는 인식되지 않습니다).\r\n");
                return;
            }

            // 땅에 딸린 사실만 채운다. 건축면적·면적표는 설계 결과물이라 사용자가 입력한다.
            var filled = new List<string>();
            if (index.Zones.Count > 0)
            {
                _useZones.Text = string.Join(", ", index.Zones);
                filled.Add($"지역/지구 {index.Zones.Count}건");
            }
            if (index.Area is double area)
            {
                _siteArea.Text = area.ToString("0.##");
                filled.Add($"대지면적 {area:N2}㎡" + (index.Category.Length > 0 ? $" ({index.Category})" : ""));
            }
            if (index.Province.Length > 0)
            {
                _province.Text = index.Province;
                _city.Text = index.City;
                filled.Add($"지자체 {index.Province} {index.City}");
            }

            _log.AppendText(filled.Count > 0
                ? $"{DateTime.Now:HH:mm:ss}  자동조회 완료 (PNU {index.Pnu}) — {string.Join(" / ", filled)}\r\n"
                : $"{DateTime.Now:HH:mm:ss}  자동조회: 채울 정보를 찾지 못했습니다 (PNU {index.Pnu}).\r\n");
            if (index.Zones.Count > 0)
                _log.AppendText($"{DateTime.Now:HH:mm:ss}    지역/지구: {_useZones.Text}\r\n");
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
            var checklist = ChecklistLoader.LoadDefault();

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
            DistrictPlanFiles = _districtPlanFiles.ToList(),
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
