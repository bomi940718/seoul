using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorkReport.Core.Models
{
    /// <summary>
    /// 대시보드 그룹 1개와, 그 그룹으로 볼 일지 값들.
    ///
    /// 그룹 이름 자체가 항상 키워드로 쓰이고, 이름과 일지 표기가 다를 때만 키워드를 추가한다.
    /// 예: LUNCHING 그룹에 키워드 BRANDING → 일지의 BRANDING-1·BRANDING-2가 LUNCHING으로 간다.
    /// </summary>
    [JsonConverter(typeof(GroupRuleConverter))]
    public class GroupRule
    {
        public GroupRule() { }

        public GroupRule(string name, params string[] keywords)
        {
            Name = name;
            Keywords = keywords?.ToList() ?? new List<string>();
        }

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("keywords")]
        public List<string> Keywords { get; set; } = new List<string>();

        /// <summary>이름 + 키워드 (빈 값 제외).</summary>
        [JsonIgnore]
        public IEnumerable<string> AllKeywords
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name)) yield return Name;
                foreach (var k in Keywords ?? new List<string>())
                    if (!string.IsNullOrWhiteSpace(k)) yield return k;
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// 키워드가 없으면 문자열 하나로, 있으면 객체로 저장한다.
    /// 예전 형식(그룹 이름만 나열한 문자열 배열)도 그대로 읽는다.
    /// </summary>
    public class GroupRuleConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(GroupRule);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            if (token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.String)
                return new GroupRule { Name = token.Value<string>() ?? "" };

            var obj = (JObject)token;
            var rule = new GroupRule { Name = (string)obj["name"] ?? "" };
            var keywords = obj["keywords"] as JArray;
            if (keywords != null)
                rule.Keywords = keywords.Select(k => (string)k).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
            return rule;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var rule = (GroupRule)value;
            if (rule.Keywords == null || rule.Keywords.Count == 0)
            {
                writer.WriteValue(rule.Name);
                return;
            }
            writer.WriteStartObject();
            writer.WritePropertyName("name");
            writer.WriteValue(rule.Name);
            writer.WritePropertyName("keywords");
            writer.WriteStartArray();
            foreach (var k in rule.Keywords) writer.WriteValue(k);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
