using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorkReport.AddIn.Services;

namespace WorkReport.AddIn.UI
{
    /// <summary>리포트 갱신 결과 요약 창: 프로젝트별 본인/협업자 행 수, 경고, [대시보드 열기].</summary>
    public class ResultWindow : Window
    {
        private readonly RefreshResult _result;

        public ResultWindow(RefreshResult result)
        {
            _result = result;

            Title = "워크리포트 — 갱신 결과";
            Width = 560;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontSize = 13;

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 상단 요약
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new TextBlock
            {
                Text = $"갱신 시각: {result.GeneratedAt:yyyy-MM-dd HH:mm}   프로젝트 {result.Projects.Count}건",
                FontWeight = FontWeights.Bold,
            });
            header.Children.Add(new TextBlock
            {
                Text = (result.NasCopyOk ? "출력 위치: " : "출력 위치(로컬): ") + result.FinalOutputDir,
                TextWrapping = TextWrapping.Wrap,
                Foreground = result.NasCopyOk ? Brushes.Black : Brushes.DarkOrange,
            });
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // 프로젝트별 행 수
            var list = new ListView { ItemsSource = result.Projects };
            var gv = new GridView();
            gv.Columns.Add(MakeColumn("넘버", nameof(ProjectSummary.Number), 175));
            gv.Columns.Add(MakeColumn("이름", nameof(ProjectSummary.Name), 160));
            gv.Columns.Add(MakeColumn("본인", nameof(ProjectSummary.MyCount), 55));
            gv.Columns.Add(MakeColumn("협업자", nameof(ProjectSummary.PartnerCount), 55));
            gv.Columns.Add(MakeColumn("합계", nameof(ProjectSummary.Total), 55));
            list.View = gv;
            Grid.SetRow(list, 1);
            root.Children.Add(list);

            // 경고 + 미등록 키
            var notices = new StackPanel { Margin = new Thickness(0, 10, 0, 0), MaxHeight = 140 };
            if (result.UnregisteredKeys.Count > 0)
            {
                // 원본 키에 셀 내 줄바꿈이 있어도 한 줄 흐름이 끊기지 않게 공백으로 정리해 표시
                string keys = string.Join(", ",
                    result.UnregisteredKeys.Select(k => $"{CollapseSpace(k.Number)}({k.Count}건)"));
                notices.Children.Add(new TextBlock
                {
                    Text = "미등록 프로젝트 키: " + keys + " — [프로젝트 관리]에서 등록하세요.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.DarkOrange,
                });
            }
            if (result.RemovedFiles.Count > 0)
            {
                notices.Children.Add(new TextBlock
                {
                    Text = "정리된 옛 파일: " + string.Join(", ", result.RemovedFiles.Distinct()),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }
            foreach (var w in result.Warnings)
            {
                notices.Children.Add(new TextBlock
                {
                    Text = "⚠ " + w,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.Firebrick,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }
            var noticeScroll = new ScrollViewer
            {
                Content = notices,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            Grid.SetRow(noticeScroll, 2);
            root.Children.Add(noticeScroll);

            // 버튼
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var openBtn = new Button
            {
                Content = "대시보드 열기",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true,
            };
            openBtn.Click += OnOpenDashboard;
            var closeBtn = new Button
            {
                Content = "닫기",
                Padding = new Thickness(14, 6, 14, 6),
                IsCancel = true,
            };
            buttons.Children.Add(openBtn);
            buttons.Children.Add(closeBtn);
            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            Content = root;
        }

        private static string CollapseSpace(string s)
            => System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim();

        private static GridViewColumn MakeColumn(string header, string property, double width)
        {
            return new GridViewColumn
            {
                Header = header,
                Width = width,
                DisplayMemberBinding = new System.Windows.Data.Binding(property),
            };
        }

        private void OnOpenDashboard(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_result.DashboardPath))
                {
                    Process.Start(_result.DashboardPath);
                    Close();
                }
                else
                {
                    MessageBox.Show("대시보드 파일을 찾을 수 없습니다: " + _result.DashboardPath,
                        "워크리포트", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("대시보드 열기 실패", ex);
                MessageBox.Show("대시보드를 열지 못했습니다: " + ex.Message,
                    "워크리포트", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
