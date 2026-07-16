using System;
using System.IO;
using Newtonsoft.Json;

namespace WorkReport.Core.Config
{
    /// <summary>%LOCALAPPDATA%\WorkReport\settings.json — PC별 로컬 설정.</summary>
    public class LocalSettings
    {
        [JsonProperty("myJournalPath")]
        public string MyJournalPath { get; set; } = "";

        [JsonProperty("myAuthorName")]
        public string MyAuthorName { get; set; } = "본인";

        [JsonProperty("partnerJournalPath")]
        public string PartnerJournalPath { get; set; } = "";

        [JsonProperty("partnerAuthorName")]
        public string PartnerAuthorName { get; set; } = "협업자";

        /// <summary>NAS 공유설정 폴더 (projects.json 위치).</summary>
        [JsonProperty("sharedConfigDir")]
        public string SharedConfigDir { get; set; } = "";

        /// <summary>NAS 공통 출력 루트 (HTML 저장 위치).</summary>
        [JsonProperty("outputRootDir")]
        public string OutputRootDir { get; set; } = "";

        /// <summary>대상 시트명. 비우면 현재 연도.</summary>
        [JsonProperty("sheetName")]
        public string SheetName { get; set; } = "";

        /// <summary>내 일지 저장 시 리포트 자동 갱신 (WorkbookAfterSave 훅).</summary>
        [JsonProperty("autoRefreshOnSave")]
        public bool AutoRefreshOnSave { get; set; } = false;

        [JsonIgnore]
        public string EffectiveSheetName =>
            string.IsNullOrWhiteSpace(SheetName) ? DateTime.Now.Year.ToString() : SheetName.Trim();

        public static string DefaultDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkReport");

        public static string DefaultPath => Path.Combine(DefaultDir, "settings.json");

        public static LocalSettings Load(string path = null)
        {
            path = path ?? DefaultPath;
            if (!File.Exists(path)) return new LocalSettings();
            return JsonConvert.DeserializeObject<LocalSettings>(File.ReadAllText(path)) ?? new LocalSettings();
        }

        public void Save(string path = null)
        {
            path = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }
}
