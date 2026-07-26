using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WorkReport.Core.Reporting
{
    /// <summary>
    /// 출력 폴더에 "이번에 생성한 파일 목록"을 남겨, 다음 갱신 때 더 이상 만들어지지 않는
    /// 파일(프로젝트 이름 변경 등으로 이름이 바뀐 옛 HTML)을 정리한다.
    ///
    /// 목록에 있는 파일만 지우므로, 사용자가 같은 폴더에 둔 다른 파일은 절대 건드리지 않는다.
    /// </summary>
    public class OutputManifest
    {
        public const string FileName = ".workreport-files.json";

        [JsonProperty("files")]
        public List<string> Files { get; set; } = new List<string>();

        public static string PathIn(string dir) => Path.Combine(dir, FileName);

        public static OutputManifest Load(string dir)
        {
            string path = PathIn(dir);
            if (!File.Exists(path)) return new OutputManifest();
            try
            {
                return JsonConvert.DeserializeObject<OutputManifest>(File.ReadAllText(path))
                       ?? new OutputManifest();
            }
            catch
            {
                // 목록이 깨졌으면 이번 실행분만 새로 기록한다 (삭제는 하지 않음)
                return new OutputManifest();
            }
        }

        public void Save(string dir)
        {
            string path = PathIn(dir);
            Directory.CreateDirectory(dir);
            if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            try
            {
                File.SetAttributes(path, FileAttributes.Hidden);
            }
            catch
            {
                // 숨김 속성은 폴더를 깔끔하게 보이려는 것뿐이라 실패해도 무시
            }
        }

        /// <summary>
        /// 이전 목록에는 있으나 이번에 생성되지 않은 파일을 지우고, 목록을 갱신한다.
        /// 반환값은 실제로 지운 파일명. 삭제 실패는 warnings에만 남기고 진행한다.
        /// </summary>
        public static List<string> Prune(string dir, IEnumerable<string> currentFileNames, IList<string> warnings)
        {
            var current = new HashSet<string>(currentFileNames, StringComparer.OrdinalIgnoreCase);
            var previous = Load(dir);
            var removed = new List<string>();

            foreach (string name in previous.Files ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(name) || current.Contains(name)) continue;
                // 경로 조작 방지: 파일명만 허용한다
                if (name.IndexOfAny(new[] { '\\', '/', ':' }) >= 0) continue;

                string path = Path.Combine(dir, name);
                if (!File.Exists(path)) continue;
                try
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    removed.Add(name);
                }
                catch (Exception ex)
                {
                    warnings?.Add($"더 이상 쓰이지 않는 파일을 지우지 못했습니다: {path} ({ex.Message})");
                }
            }

            new OutputManifest { Files = current.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList() }
                .Save(dir);

            return removed;
        }
    }
}
