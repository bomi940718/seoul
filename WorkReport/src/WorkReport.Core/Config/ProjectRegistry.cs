using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using WorkReport.Core.Models;

namespace WorkReport.Core.Config
{
    /// <summary>
    /// {공유설정폴더}\projects.json — 두 사용자가 공유하는 프로젝트 등록 파일.
    /// 동시 수정 대비: 로드 시점 타임스탬프를 기억하고, 저장 전에 변경 여부를 검사한다 (락 없음, 경고 수준).
    /// </summary>
    public class ProjectRegistry
    {
        public const string FileName = "projects.json";

        public List<ProjectInfo> Projects { get; set; } = new List<ProjectInfo>();

        [JsonIgnore]
        public string LoadedFrom { get; private set; }

        [JsonIgnore]
        public DateTime LoadedTimestampUtc { get; private set; }

        public static string PathIn(string sharedConfigDir) => Path.Combine(sharedConfigDir, FileName);

        public static ProjectRegistry Load(string sharedConfigDir)
        {
            string path = PathIn(sharedConfigDir);
            var reg = new ProjectRegistry { LoadedFrom = path };
            if (File.Exists(path))
            {
                var loaded = JsonConvert.DeserializeObject<ProjectRegistry>(File.ReadAllText(path));
                if (loaded?.Projects != null) reg.Projects = loaded.Projects;
                reg.LoadedTimestampUtc = File.GetLastWriteTimeUtc(path);
            }
            return reg;
        }

        /// <summary>로드 이후 다른 사용자가 파일을 수정했으면 true (저장 전 재로드 안내용).</summary>
        public bool HasExternalChange()
        {
            if (LoadedFrom == null || !File.Exists(LoadedFrom)) return false;
            return File.GetLastWriteTimeUtc(LoadedFrom) > LoadedTimestampUtc;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(LoadedFrom))
                throw new InvalidOperationException("저장 경로가 없습니다. Load()로 먼저 열어야 합니다.");
            Directory.CreateDirectory(Path.GetDirectoryName(LoadedFrom));
            File.WriteAllText(LoadedFrom, JsonConvert.SerializeObject(this, Formatting.Indented));
            LoadedTimestampUtc = File.GetLastWriteTimeUtc(LoadedFrom);
        }
    }
}
