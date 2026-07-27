using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WorkReport.AddIn.Services;
using WorkReport.Core.Config;
using WorkReport.Core.Models;
using WorkReport.Core.Parsing;
using WorkReport.Core.Reporting;

namespace WorkReport.AddIn.UI
{
    /// <summary>별칭 목록 ↔ "A, B" 문자열 (그리드에서 편집하기 위한 변환).</summary>
    public class AliasListConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var list = value as List<string>;
            return list == null ? "" : string.Join(", ", list);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ((value as string) ?? "")
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }
    }

    /// <summary>프로젝트 관리 창 — projects.json 편집 + 미등록 키 감지.</summary>
    public class ProjectsWindow : Window
    {
        private readonly LocalSettings _settings;
        private ProjectRegistry _registry;

        private readonly ObservableCollection<ProjectInfo> _items = new ObservableCollection<ProjectInfo>();
        private readonly ObservableCollection<UnregisteredKey> _unregistered = new ObservableCollection<UnregisteredKey>();

        private readonly DataGrid _grid = new DataGrid();
        private readonly ListView _unregisteredList = new ListView();
        private readonly TextBlock _scanStatus = new TextBlock { Foreground = Brushes.Gray, FontSize = 11 };
        private readonly Button _scanButton = new Button { Content = "일지에서 미등록 키 검색", Padding = new Thickness(10, 4, 10, 4) };
        private readonly Button _mergeButton = new Button
        {
            Content = "H·I 뒤바뀐 쌍 합치기…",
            Padding = new Thickness(10, 4, 10, 4),
            IsEnabled = false,
        };
        private readonly TextBox _groupsBox = new TextBox { Padding = new Thickness(4, 3, 4, 3) };

        /// <summary>마지막 스캔에서 찾은 레코드 (뒤바뀐 쌍 병합에 재사용).</summary>
        private List<WorkRecord> _scannedRecords = new List<WorkRecord>();
        private List<MirrorPair> _mirrorPairs = new List<MirrorPair>();

        /// <summary>저장하고 닫혔는지. DialogResult는 ShowDialog로 띄운 창에서만 대입할 수 있어 사용하지 않는다.</summary>
        public bool Saved { get; private set; }

        public ProjectsWindow(LocalSettings settings)
        {
            _settings = settings;

            Title = "워크리포트 — 프로젝트 관리";
            Width = 1040;
            Height = 620;
            MinWidth = 760;
            MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontSize = 13;

            LoadRegistry();

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            header.Children.Add(new TextBlock
            {
                Text = "등록 파일: " + (_registry.LoadedFrom ?? "(경로 없음)"),
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
            });

            header.Children.Add(new TextBlock
            {
                Text = "그룹 규칙 — 한 줄에 하나. 이름만 적거나, 일지 표기가 다르면 \"이름 = 키워드, 키워드\"",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 6, 0, 3),
            });

            _groupsBox.Text = GroupResolver.Format(_registry.Groups);
            _groupsBox.AcceptsReturn = true;
            _groupsBox.TextWrapping = TextWrapping.NoWrap;
            _groupsBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _groupsBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _groupsBox.Height = 92;
            _groupsBox.FontFamily = new FontFamily("Consolas, D2Coding, 맑은 고딕");
            _groupsBox.ToolTip =
                "프로젝트의 그룹 칸이 비어 있으면, 일지의 H·I 열에서 이 키워드를 찾아 그룹을 자동 배정합니다.\n"
                + "그룹 이름 자체도 키워드로 쓰이며, 위에 적은 줄이 우선합니다.\n"
                + "예) LUNCHING = BRANDING  →  일지의 BRANDING-1·BRANDING-2가 LUNCHING 탭으로";
            header.Children.Add(_groupsBox);
            header.Children.Add(new TextBlock
            {
                Text = "아래 표의 그룹 칸을 직접 채우면 그 값이 규칙보다 우선합니다. 아무 규칙에도 걸리지 않으면 ETC.",
                Foreground = Brushes.Gray,
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            root.Children.Add(Row(0, header));

            // 본문: 왼쪽 그리드 / 오른쪽 미등록 키
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3.0, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
            body.Children.Add(Column(0, BuildGridPanel()));
            body.Children.Add(Column(2, BuildUnregisteredPanel()));
            root.Children.Add(Row(1, body));

            root.Children.Add(Row(2, BuildButtonBar()));

            Content = root;
            Loaded += (s, e) => StartScan();
        }

        private static UIElement Row(int row, UIElement child) { Grid.SetRow(child, row); return child; }
        private static UIElement Column(int col, UIElement child) { Grid.SetColumn(child, col); return child; }

        private void LoadRegistry()
        {
            _registry = ProjectRegistry.Load(_settings.SharedConfigDir);
            _items.Clear();
            foreach (var p in _registry.Projects) _items.Add(p);
        }

        private UIElement BuildGridPanel()
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _grid.ItemsSource = _items;
            _grid.AutoGenerateColumns = false;
            _grid.CanUserAddRows = false;      // 기본값 지정을 위해 [추가] 버튼으로만 행을 만든다
            _grid.CanUserDeleteRows = false;
            _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            _grid.GridLinesVisibility = DataGridGridLinesVisibility.All;
            _grid.SelectionMode = DataGridSelectionMode.Extended;
            // 고정 너비 합계가 패널 폭에 맞게 잡혀 있다. 창을 줄이면 star 대신 가로 스크롤로 처리한다.
            _grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _grid.Columns.Add(TextColumn("넘버 (일지 I열 매칭 키)", nameof(ProjectInfo.Number), 155));
            _grid.Columns.Add(TextColumn("이름", nameof(ProjectInfo.Name), 130));
            _grid.Columns.Add(TextColumn("그룹", nameof(ProjectInfo.Group), 90));
            var aliasCol = new DataGridTextColumn
            {
                Header = "같은 프로젝트 넘버",
                Width = 140,
                Binding = new Binding(nameof(ProjectInfo.Aliases))
                {
                    Converter = new AliasListConverter(),
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                },
            };
            _grid.Columns.Add(aliasCol);
            _grid.Columns.Add(StatusColumn());
            _grid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "활성",
                Width = 50,
                Binding = new Binding(nameof(ProjectInfo.Active)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            });
            // 마지막 열은 남는 폭을 채워 가로 스크롤이 생기지 않게 한다
            var outCol = TextColumn("개별 출력 폴더", nameof(ProjectInfo.OutputDir), 155);
            outCol.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            outCol.MinWidth = 120;
            _grid.Columns.Add(outCol);
            panel.Children.Add(Row(0, _grid));

            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var add = new Button { Content = "＋ 추가", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
            add.Click += (s, e) => AddProject(new ProjectInfo
            {
                Number = "",
                Name = "",
                Group = "ETC",
                Status = ProjectInfo.StatusActive,
                Active = true,
            });
            var del = new Button { Content = "－ 삭제", Padding = new Thickness(12, 4, 12, 4) };
            del.Click += OnDelete;
            bar.Children.Add(add);
            bar.Children.Add(del);
            bar.Children.Add(new TextBlock
            {
                Text = "  그룹은 대시보드 탭 이름입니다 (영어 대문자 권장). 완료 상태는 대시보드에서 기본 숨김.",
                Foreground = Brushes.Gray,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });
            panel.Children.Add(Row(1, bar));

            return panel;
        }

        private static DataGridTextColumn TextColumn(string header, string property, double width) => new DataGridTextColumn
        {
            Header = header,
            Width = width,
            Binding = new Binding(property) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        };

        private static DataGridComboBoxColumn StatusColumn()
        {
            var col = new DataGridComboBoxColumn
            {
                Header = "상태",
                Width = 72,
                SelectedItemBinding = new Binding(nameof(ProjectInfo.Status)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            };
            col.ItemsSource = new[] { ProjectInfo.StatusPlanned, ProjectInfo.StatusActive, ProjectInfo.StatusDone };
            return col;
        }

        private UIElement BuildUnregisteredPanel()
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            panel.Children.Add(Row(0, new TextBlock
            {
                Text = "미등록 키 (일지에는 있으나 미등록)",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6),
            }));

            _unregisteredList.ItemsSource = _unregistered;
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "넘버", Width = 118, DisplayMemberBinding = new Binding(nameof(UnregisteredKey.Number)) });
            gv.Columns.Add(new GridViewColumn { Header = "건수", Width = 42, DisplayMemberBinding = new Binding(nameof(UnregisteredKey.Count)) });
            gv.Columns.Add(new GridViewColumn { Header = "예시 이름", Width = 100, DisplayMemberBinding = new Binding(nameof(UnregisteredKey.SampleProjectName)) });
            _unregisteredList.View = gv;
            _unregisteredList.MouseDoubleClick += (s, e) => RegisterSelectedKey();
            panel.Children.Add(Row(1, _unregisteredList));

            var bottom = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var register = new Button { Content = "선택 항목 등록하기 →", Padding = new Thickness(10, 4, 10, 4) };
            register.Click += (s, e) => RegisterSelectedKey();
            bottom.Children.Add(register);
            _mergeButton.Margin = new Thickness(0, 6, 0, 0);
            _mergeButton.Click += (s, e) => MergeMirrorPairs();
            bottom.Children.Add(_mergeButton);
            _scanButton.Margin = new Thickness(0, 6, 0, 0);
            _scanButton.Click += (s, e) => StartScan();
            bottom.Children.Add(_scanButton);
            _scanStatus.Margin = new Thickness(0, 6, 0, 0);
            _scanStatus.TextWrapping = TextWrapping.Wrap;
            bottom.Children.Add(_scanStatus);
            panel.Children.Add(Row(2, bottom));

            return panel;
        }

        private UIElement BuildButtonBar()
        {
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var save = new Button { Content = "저장", Padding = new Thickness(20, 6, 20, 6), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            save.Click += OnSave;
            var cancel = new Button { Content = "취소", Padding = new Thickness(20, 6, 20, 6), IsCancel = true };
            bar.Children.Add(save);
            bar.Children.Add(cancel);
            return bar;
        }

        private void AddProject(ProjectInfo p)
        {
            _items.Add(p);
            _grid.SelectedItem = p;
            _grid.ScrollIntoView(p);
            _grid.Focus();
        }

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            var selected = _grid.SelectedItems.Cast<ProjectInfo>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("삭제할 행을 선택하세요.", "워크리포트", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string names = string.Join(", ", selected.Take(5).Select(p => string.IsNullOrWhiteSpace(p.Number) ? "(넘버 없음)" : p.Number));
            if (selected.Count > 5) names += $" 외 {selected.Count - 5}건";
            var answer = MessageBox.Show(
                $"{selected.Count}건을 목록에서 삭제할까요?\n\n{names}\n\n" +
                "※ 일지 데이터는 그대로이며, 저장해야 실제로 반영됩니다.\n" +
                "   기록을 남겨두려면 삭제 대신 [활성] 체크를 해제하세요.",
                "워크리포트 — 프로젝트 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            foreach (var p in selected) _items.Remove(p);
        }

        private void RegisterSelectedKey()
        {
            var key = _unregisteredList.SelectedItem as UnregisteredKey;
            if (key == null)
            {
                MessageBox.Show("등록할 미등록 키를 선택하세요.", "워크리포트", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            AddProject(new ProjectInfo
            {
                Number = key.Number,
                Name = key.SampleProjectName ?? "",
                Group = "",     // 비워두면 그룹 이름 목록으로 자동 배정된다
                Status = ProjectInfo.StatusActive,
                Active = true,
            });
            _unregistered.Remove(key);
        }

        /// <summary>
        /// H·I 열을 바꿔 적어 갈라진 쌍을 한 프로젝트로 묶는다.
        /// 기록이 많은 쪽을 남기고 반대쪽을 별칭으로 넣으며, 반대쪽이 따로 등록돼 있으면 그 행은 지운다.
        /// 실제 반영은 [저장]을 눌러야 하고, 엑셀 일지는 건드리지 않는다.
        /// </summary>
        private void MergeMirrorPairs()
        {
            if (_mirrorPairs.Count == 0)
            {
                MessageBox.Show("뒤바뀐 쌍이 없습니다.", "워크리포트", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _grid.CommitEdit(DataGridEditingUnit.Row, true);

            var plan = new List<string>();
            var actions = new List<Action>();
            var conflicts = new List<string>();

            foreach (var pair in _mirrorPairs)
            {
                var projA = FindItem(pair.Primary);
                var projB = FindItem(pair.Secondary);

                // 이미 같은 프로젝트로 묶여 있음
                if (projA != null && ReferenceEquals(projA, projB)) continue;

                if (projA == null && projB == null)
                {
                    // 둘 다 미등록 — 많은 쪽을 넘버로, 반대쪽을 별칭으로 새로 만든다
                    var p = pair;
                    plan.Add($"새로 등록: {p.Primary} (+{p.Secondary})   {p.Total}건");
                    actions.Add(() => _items.Add(new ProjectInfo
                    {
                        Number = p.Primary,
                        Name = SampleNameFor(p.Primary),
                        Group = "",
                        Status = ProjectInfo.StatusActive,
                        Active = true,
                        Aliases = new List<string> { p.Secondary },
                    }));
                    continue;
                }

                if (projA != null && projB != null)
                {
                    // 양쪽 다 등록돼 있다. 한쪽이 "그 넘버 자체로 등록된 행"일 때만 안전하게 합칠 수 있다.
                    // 그렇지 않으면 이 넘버가 다른 프로젝트에서도 쓰이고 있다는 뜻이라 건드리지 않는다.
                    var keepBoth = projA;
                    var dropBoth = projB;
                    string aliasBoth = pair.Secondary;
                    if (!IsOwnNumber(dropBoth, pair.Secondary) || !IsOwnNumber(keepBoth, pair.Primary))
                    {
                        conflicts.Add($"{pair.Primary} ↔ {pair.Secondary}: " +
                                      $"\"{pair.Secondary}\" 을(를) 다른 프로젝트도 쓰고 있어 건너뜁니다");
                        continue;
                    }
                    plan.Add($"합치기: {keepBoth.Number} ← {aliasBoth}   ({pair.Total}건, 중복 행 삭제)");
                    actions.Add(() =>
                    {
                        if (keepBoth.Aliases == null) keepBoth.Aliases = new List<string>();
                        keepBoth.Aliases.Add(aliasBoth);
                        _items.Remove(dropBoth);
                    });
                    continue;
                }

                // 한쪽만 등록됨 — 나머지를 별칭으로 붙인다
                var keep = projA ?? projB;
                string alias = projA != null ? pair.Secondary : pair.Primary;
                plan.Add($"별칭 추가: {keep.Number} ← {alias}   ({pair.Total}건)");
                actions.Add(() =>
                {
                    if (keep.Aliases == null) keep.Aliases = new List<string>();
                    keep.Aliases.Add(alias);
                });
            }

            if (plan.Count == 0)
            {
                MessageBox.Show(
                    conflicts.Count == 0
                        ? "이미 모두 합쳐져 있습니다."
                        : "합칠 수 있는 쌍이 없습니다.\n\n" + string.Join("\n", conflicts),
                    "워크리포트", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string message = "다음과 같이 합칩니다:\n\n" + string.Join("\n", plan);
            if (conflicts.Count > 0)
                message += "\n\n건너뛰는 항목:\n" + string.Join("\n", conflicts);
            message += "\n\n엑셀 일지는 전혀 바뀌지 않습니다. [저장]을 눌러야 실제로 반영됩니다.\n\n진행할까요?";

            var answer = MessageBox.Show(message,
                "워크리포트 — 뒤바뀐 쌍 합치기", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            foreach (var act in actions) act();
            _grid.Items.Refresh();
            Logger.Info($"뒤바뀐 쌍 병합: {plan.Count}건 — {string.Join(" / ", plan)}");
            StartScan();   // 미등록 목록·쌍 목록을 다시 계산
        }

        private ProjectInfo FindItem(string number)
        {
            string key = KeyNormalizer.Normalize(number);
            return _items.FirstOrDefault(p => p.AllNumbers.Any(n => KeyNormalizer.Normalize(n) == key));
        }

        /// <summary>그 넘버가 이 프로젝트의 대표 넘버인가 (별칭이 아니라).</summary>
        private static bool IsOwnNumber(ProjectInfo p, string number)
            => KeyNormalizer.Normalize(p.Number) == KeyNormalizer.Normalize(number);

        /// <summary>넘버에 해당하는 H열 값 하나를 이름 기본값으로 가져온다.</summary>
        private string SampleNameFor(string number)
        {
            string key = KeyNormalizer.Normalize(number);
            var rec = _scannedRecords.FirstOrDefault(
                r => KeyNormalizer.Normalize(r.ProjectNumber) == key && !string.IsNullOrWhiteSpace(r.ProjectName));
            return rec == null ? "" : rec.ProjectName.Trim();
        }

        /// <summary>일지를 백그라운드에서 파싱해 미등록 키 목록을 채운다 (수 초 소요).</summary>
        private void StartScan()
        {
            _scanButton.IsEnabled = false;
            _unregistered.Clear();
            _scanStatus.Text = "일지를 읽는 중…";
            _scanStatus.Foreground = Brushes.Gray;

            _mergeButton.IsEnabled = false;
            var registered = _items.Select(p => new ProjectInfo { Number = p.Number, Aliases = p.Aliases }).ToList();
            var settings = _settings;

            Task.Run(() =>
            {
                var warnings = new List<string>();
                var records = RefreshService.ParseJournals(settings, warnings);
                var keys = ReportBuilder.FindUnregisteredKeys(records, registered);
                var mirrors = ReportBuilder.FindMirrorPairs(records);
                return new { Keys = keys, Warnings = warnings, Total = records.Count, Records = records, Mirrors = mirrors };
            })
            .ContinueWith(t =>
            {
                _scanButton.IsEnabled = true;
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.GetBaseException();
                    Logger.Error("미등록 키 검색 실패", ex);
                    _scanStatus.Text = "검색 실패: " + (ex?.Message ?? "알 수 없는 오류");
                    _scanStatus.Foreground = Brushes.Firebrick;
                    return;
                }
                foreach (var k in t.Result.Keys) _unregistered.Add(k);
                _scannedRecords = t.Result.Records;
                _mirrorPairs = t.Result.Mirrors;
                _mergeButton.IsEnabled = _mirrorPairs.Count > 0;
                _mergeButton.Content = _mirrorPairs.Count > 0
                    ? $"H·I 뒤바뀐 쌍 {_mirrorPairs.Count}건 합치기…"
                    : "H·I 뒤바뀐 쌍 없음";

                _scanStatus.Text = t.Result.Keys.Count == 0
                    ? $"레코드 {t.Result.Total}건 — 미등록 키 없음"
                    : $"레코드 {t.Result.Total}건 — 미등록 {t.Result.Keys.Count}종";
                if (t.Result.Warnings.Count > 0)
                {
                    _scanStatus.Text += "\n⚠ " + string.Join("\n⚠ ", t.Result.Warnings);
                    _scanStatus.Foreground = Brushes.DarkOrange;
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            _grid.CommitEdit(DataGridEditingUnit.Row, true);

            var cleaned = new List<ProjectInfo>();
            var seen = new Dictionary<string, string>();
            foreach (var p in _items)
            {
                string number = (p.Number ?? "").Trim();
                if (number.Length == 0)
                {
                    MessageBox.Show("넘버가 비어 있는 행이 있습니다. 넘버는 일지 I열과 맞추는 필수 항목입니다.",
                        "워크리포트", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                string key = KeyNormalizer.Normalize(number);
                if (seen.ContainsKey(key))
                {
                    MessageBox.Show($"넘버가 중복됩니다: \"{number}\" 와 \"{seen[key]}\"\n" +
                                    "(대소문자·공백·줄바꿈 차이는 같은 키로 취급됩니다.)",
                        "워크리포트", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                seen[key] = number;

                p.Number = number;
                p.Name = (p.Name ?? "").Trim();
                // 그룹은 비워둘 수 있다 — 비면 그룹 이름 목록으로 자동 배정된다
                p.Group = (p.Group ?? "").Trim();
                p.Status = p.EffectiveStatus;
                p.OutputDir = string.IsNullOrWhiteSpace(p.OutputDir) ? null : p.OutputDir.Trim();

                // 별칭: 공백 제거, 자기 넘버·중복 제외
                var aliases = new List<string>();
                foreach (var raw in p.Aliases ?? new List<string>())
                {
                    string a = (raw ?? "").Trim();
                    if (a.Length == 0) continue;
                    string ak = KeyNormalizer.Normalize(a);
                    if (ak == key || aliases.Any(x => KeyNormalizer.Normalize(x) == ak)) continue;
                    if (seen.ContainsKey(ak))
                    {
                        MessageBox.Show($"\"{a}\" 은(는) 이미 \"{seen[ak]}\" 에서 쓰고 있습니다.\n" +
                                        "같은 넘버를 두 프로젝트가 함께 쓸 수는 없습니다.",
                            "워크리포트", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    aliases.Add(a);
                    seen[ak] = number;
                }
                p.Aliases = aliases;

                cleaned.Add(p);
            }

            var groupRules = GroupResolver.Parse(_groupsBox.Text);

            // 락 없는 공유 파일이므로, 로드 이후 상대가 수정했으면 덮어쓰기 전에 경고한다
            if (_registry.HasExternalChange())
            {
                var answer = MessageBox.Show(
                    "이 창을 연 뒤 다른 사용자가 projects.json을 수정했습니다.\n\n" +
                    "[예] 내 편집 내용으로 덮어씁니다 (상대 변경분은 사라집니다)\n" +
                    "[아니오] 저장하지 않고 상대 변경분을 다시 불러옵니다 (내 편집 내용은 사라집니다)",
                    "워크리포트 — 변경 충돌", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes)
                {
                    LoadRegistry();
                    StartScan();
                    Logger.Warn("projects.json 외부 변경 감지 → 재로드 (내 편집 취소)");
                    return;
                }
                Logger.Warn("projects.json 외부 변경 감지 → 사용자 선택으로 덮어쓰기");
            }

            // 저장 실패만 오류로 보고한다. 창을 닫는 과정의 문제까지 같은 try에 넣으면
            // 파일이 이미 기록됐는데도 "저장하지 못했습니다"라고 잘못 알리게 된다.
            try
            {
                _registry.Projects = cleaned;
                _registry.Groups = groupRules;
                _registry.Save();
                Logger.Info($"projects.json 저장: {cleaned.Count}건 (활성 {cleaned.Count(p => p.Active)}건), " +
                            $"그룹 [{string.Join(" | ", groupRules.Select(g => g.Name))}]");
            }
            catch (Exception ex)
            {
                Logger.Error("projects.json 저장 실패", ex);
                MessageBox.Show("프로젝트 목록을 저장하지 못했습니다: " + ex.Message,
                    "워크리포트", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Saved = true;
            Close();
        }
    }
}
