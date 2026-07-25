using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WorkReport.AddIn.Services;
using WorkReport.Core.Config;

namespace WorkReport.AddIn.UI
{
    /// <summary>설정 창 — LocalSettings 전 항목 (PC별 로컬 설정).</summary>
    public class SettingsWindow : Window
    {
        private readonly LocalSettings _settings;

        private readonly TextBox _myPath = new TextBox();
        private readonly TextBox _myName = new TextBox();
        private readonly TextBox _partnerPath = new TextBox();
        private readonly TextBox _partnerName = new TextBox();
        private readonly TextBox _sharedDir = new TextBox();
        private readonly TextBox _outputRoot = new TextBox();
        private readonly TextBox _sheetName = new TextBox();
        private readonly CheckBox _autoRefresh = new CheckBox { Content = "내 일지를 저장할 때 리포트를 자동으로 갱신 (결과 창 없이 조용히 실행)" };

        /// <summary>저장하고 닫혔는지. DialogResult는 ShowDialog로 띄운 창에서만 대입할 수 있어 사용하지 않는다.</summary>
        public bool Saved { get; private set; }

        public SettingsWindow()
        {
            _settings = LocalSettings.Load();

            Title = "워크리포트 — 설정";
            Width = 720;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontSize = 13;

            var panel = new StackPanel { Margin = new Thickness(18) };

            panel.Children.Add(SectionLabel("일지 파일"));
            panel.Children.Add(PathRow("내 일지 xlsx", _myPath, _settings.MyJournalPath, isFile: true));
            panel.Children.Add(TextRow("내 작성자명", _myName, _settings.MyAuthorName,
                "리포트에 표시되는 이름입니다."));
            panel.Children.Add(PathRow("협업자 일지 xlsx", _partnerPath, _settings.PartnerJournalPath, isFile: true));
            panel.Children.Add(TextRow("협업자 작성자명", _partnerName, _settings.PartnerAuthorName, null));

            panel.Children.Add(SectionLabel("공유 폴더 (두 PC가 같은 경로를 봐야 합니다)"));
            panel.Children.Add(PathRow("공유설정 폴더", _sharedDir, _settings.SharedConfigDir, isFile: false,
                hint: "projects.json이 저장되는 폴더입니다."));
            panel.Children.Add(PathRow("출력 루트 폴더", _outputRoot, _settings.OutputRootDir, isFile: false,
                hint: "HTML 리포트와 index.html이 복사되는 NAS 폴더입니다."));

            panel.Children.Add(SectionLabel("기타"));
            panel.Children.Add(TextRow("대상 시트명", _sheetName, _settings.SheetName,
                "비우면 현재 연도(" + DateTime.Now.Year + ")를 사용합니다."));

            _autoRefresh.IsChecked = _settings.AutoRefreshOnSave;
            _autoRefresh.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(_autoRefresh);

            var logHint = new TextBlock
            {
                Text = "로그 위치: " + Logger.LogDir,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            panel.Children.Add(logHint);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0),
            };
            var save = new Button { Content = "저장", Padding = new Thickness(18, 6, 18, 6), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            save.Click += OnSave;
            var cancel = new Button { Content = "취소", Padding = new Thickness(18, 6, 18, 6), IsCancel = true };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
        }

        private static TextBlock SectionLabel(string text) => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 14, 0, 6),
        };

        private static Grid LabeledRow(string label, UIElement field, string hint)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition());
            if (hint != null) grid.RowDefinitions.Add(new RowDefinition());

            var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0);
            Grid.SetRow(lbl, 0);
            grid.Children.Add(lbl);

            Grid.SetColumn(field, 1);
            Grid.SetRow(field, 0);
            grid.Children.Add(field);

            if (hint != null)
            {
                var hintBlock = new TextBlock
                {
                    Text = hint,
                    Foreground = Brushes.Gray,
                    FontSize = 11,
                    Margin = new Thickness(2, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                };
                Grid.SetColumn(hintBlock, 1);
                Grid.SetRow(hintBlock, 1);
                grid.Children.Add(hintBlock);
            }
            return grid;
        }

        private static Grid TextRow(string label, TextBox box, string value, string hint)
        {
            box.Text = value ?? "";
            box.Padding = new Thickness(4, 3, 4, 3);
            return LabeledRow(label, box, hint);
        }

        private Grid PathRow(string label, TextBox box, string value, bool isFile, string hint = null)
        {
            box.Text = value ?? "";
            box.Padding = new Thickness(4, 3, 4, 3);

            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(box, 0);
            inner.Children.Add(box);

            var browse = new Button
            {
                Content = "찾아보기…",
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(6, 0, 0, 0),
            };
            browse.Click += (s, e) =>
            {
                string picked = isFile ? PickFile(box.Text) : PickFolder(box.Text);
                if (picked != null) box.Text = picked;
            };
            Grid.SetColumn(browse, 1);
            inner.Children.Add(browse);

            return LabeledRow(label, inner, hint);
        }

        private static string PickFile(string current)
        {
            var dlg = new OpenFileDialog
            {
                Title = "일지 xlsx 선택",
                Filter = "Excel 통합 문서 (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|모든 파일 (*.*)|*.*",
                CheckFileExists = true,
            };
            try
            {
                if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
                {
                    dlg.InitialDirectory = Path.GetDirectoryName(current);
                    dlg.FileName = Path.GetFileName(current);
                }
            }
            catch { /* 잘못된 경로는 무시하고 기본 위치에서 시작 */ }
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        /// <summary>WPF에는 폴더 선택 대화상자가 없어 WinForms의 FolderBrowserDialog를 사용한다.</summary>
        private static string PickFolder(string current)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "폴더 선택";
                dlg.ShowNewFolderButton = true;
                try
                {
                    if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                        dlg.SelectedPath = current;
                }
                catch { /* 잘못된 경로는 무시 */ }
                return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null;
            }
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var problems = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(_myPath.Text) && !File.Exists(_myPath.Text))
                problems.Add("내 일지 xlsx 파일이 존재하지 않습니다.");
            if (!string.IsNullOrWhiteSpace(_partnerPath.Text) && !File.Exists(_partnerPath.Text))
                problems.Add("협업자 일지 xlsx 파일이 존재하지 않습니다 (NAS 미연결 시 정상일 수 있습니다).");
            if (!string.IsNullOrWhiteSpace(_sharedDir.Text) && !Directory.Exists(_sharedDir.Text))
                problems.Add("공유설정 폴더가 존재하지 않습니다.");
            if (!string.IsNullOrWhiteSpace(_outputRoot.Text) && !Directory.Exists(_outputRoot.Text))
                problems.Add("출력 루트 폴더가 존재하지 않습니다 (갱신 시 자동 생성됩니다).");

            if (problems.Count > 0)
            {
                var answer = MessageBox.Show(
                    "다음 항목을 확인하세요:\n\n• " + string.Join("\n• ", problems) + "\n\n이대로 저장할까요?",
                    "워크리포트 — 설정", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }

            // 저장 실패만 오류로 보고한다 (ProjectsWindow와 동일한 이유)
            try
            {
                _settings.MyJournalPath = _myPath.Text.Trim();
                _settings.MyAuthorName = string.IsNullOrWhiteSpace(_myName.Text) ? "본인" : _myName.Text.Trim();
                _settings.PartnerJournalPath = _partnerPath.Text.Trim();
                _settings.PartnerAuthorName = string.IsNullOrWhiteSpace(_partnerName.Text) ? "협업자" : _partnerName.Text.Trim();
                _settings.SharedConfigDir = _sharedDir.Text.Trim();
                _settings.OutputRootDir = _outputRoot.Text.Trim();
                _settings.SheetName = _sheetName.Text.Trim();
                _settings.AutoRefreshOnSave = _autoRefresh.IsChecked == true;
                _settings.Save();

                Logger.Info($"설정 저장 (자동 갱신 {( _settings.AutoRefreshOnSave ? "켜짐" : "꺼짐")}, 시트 {_settings.EffectiveSheetName})");
            }
            catch (Exception ex)
            {
                Logger.Error("설정 저장 실패", ex);
                MessageBox.Show("설정을 저장하지 못했습니다: " + ex.Message,
                    "워크리포트", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Saved = true;
            Close();
        }
    }
}
