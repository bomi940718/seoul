using System.IO.Compression;
using System.Text;

namespace LawReview.Core.LawApi;

/// <summary>
/// HWP 5.0 문서에서 본문 텍스트만 뽑는다.
///
/// 왜 필요한가: 지자체 조례의 별표(부설주차장 설치기준 등)는 법제처 API가 내용을 주지 않고
/// HWP 첨부파일 링크만 준다. 실제 적용 기준이 그 안에 있으므로 직접 읽어야 한다.
///
/// 구조: HWP 5.0 = OLE 복합문서(CFB) → BodyText/SectionN 스트림(zlib raw deflate)
///      → 레코드 스트림에서 문단 텍스트(HWPTAG_PARA_TEXT = 67)만 UTF-16LE로 읽는다.
/// 서식·표 구조는 버리고 줄 단위 텍스트만 남긴다(값 추출에는 그걸로 충분하다).
/// </summary>
public static class HwpTextExtractor
{
    private const int HwpTagParaText = 67;

    public static bool LooksLikeHwp(ReadOnlySpan<byte> file) =>
        file.Length > 8 && BitConverter.ToUInt32(file[..4]) == 0xe011cfd0
                        && BitConverter.ToUInt32(file.Slice(4, 4)) == 0xe11ab1a1;

    /// <summary>본문 텍스트를 줄 단위로 돌려준다. 형식이 아니면 빈 목록.</summary>
    public static IReadOnlyList<string> ExtractLines(byte[] file)
    {
        if (!LooksLikeHwp(file)) return Array.Empty<string>();

        try
        {
            var cfb = new Cfb(file);
            var compressed = IsCompressed(cfb);

            var lines = new List<string>();
            foreach (var name in cfb.StreamNames
                         .Where(n => n.StartsWith("Section", StringComparison.Ordinal))
                         .OrderBy(n => int.TryParse(n[7..], out var i) ? i : 0))
            {
                var raw = cfb.Read(name);
                var body = compressed ? Inflate(raw) : raw;
                if (body.Length > 0) lines.AddRange(ReadParagraphs(body));
            }
            return lines;
        }
        catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException
                                      or ArgumentOutOfRangeException or EndOfStreamException)
        {
            return Array.Empty<string>();   // 손상·비표준 파일은 조용히 포기한다
        }
    }

    private static bool IsCompressed(Cfb cfb)
    {
        var header = cfb.Read("FileHeader");
        return header.Length < 40 || (BitConverter.ToUInt32(header, 36) & 1) == 1;
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    // 컨트롤 문자: 0/10/13 등은 1 WCHAR, 확장·인라인 컨트롤은 8 WCHAR을 차지한다.
    private static readonly HashSet<int> CharControls = new() { 0, 10, 13, 24, 25, 26, 27, 28, 29, 30, 31 };
    private static readonly HashSet<int> LongControls =
        new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23 };

    private static IEnumerable<string> ReadParagraphs(byte[] body)
    {
        var pos = 0;
        while (pos + 4 <= body.Length)
        {
            var header = BitConverter.ToUInt32(body, pos);
            pos += 4;
            var tag = (int)(header & 0x3ff);
            var size = (int)((header >> 20) & 0xfff);
            if (size == 0xfff)
            {
                if (pos + 4 > body.Length) yield break;
                size = (int)BitConverter.ToUInt32(body, pos);
                pos += 4;
            }
            if (pos + size > body.Length) yield break;

            if (tag == HwpTagParaText)
            {
                var text = DecodeParagraph(body.AsSpan(pos, size));
                foreach (var line in text.Split('\n'))
                    if (line.Trim().Length > 0) yield return line.Trim();
            }
            pos += size;
        }
    }

    private static string DecodeParagraph(ReadOnlySpan<byte> span)
    {
        var sb = new StringBuilder();
        for (var i = 0; i + 1 < span.Length; i += 2)
        {
            var c = BitConverter.ToUInt16(span.Slice(i, 2));
            if (CharControls.Contains(c)) { if (c is 10 or 13) sb.Append('\n'); continue; }
            if (LongControls.Contains(c)) { i += 14; continue; }   // 8 WCHAR = 16 byte
            sb.Append((char)c);
        }
        return sb.ToString();
    }

    /// <summary>CFB(OLE 복합문서) 최소 구현 — 스트림 읽기에 필요한 부분만.</summary>
    private sealed class Cfb
    {
        private readonly byte[] _file;
        private readonly int _sectorSize;
        private readonly int _miniSectorSize;
        private readonly uint _miniCutoff;
        private readonly List<uint> _fat = new();
        private readonly List<uint> _miniFat = new();
        private readonly Dictionary<string, (uint Start, uint Size)> _entries = new();
        private readonly byte[] _miniStream;

        public IEnumerable<string> StreamNames => _entries.Keys;

        public Cfb(byte[] file)
        {
            _file = file;
            _sectorSize = 1 << BitConverter.ToUInt16(file, 0x1e);
            _miniSectorSize = 1 << BitConverter.ToUInt16(file, 0x20);
            var numFat = BitConverter.ToUInt32(file, 0x2c);
            var dirStart = BitConverter.ToUInt32(file, 0x30);
            _miniCutoff = BitConverter.ToUInt32(file, 0x38);
            var miniFatStart = BitConverter.ToUInt32(file, 0x3c);
            var difatStart = BitConverter.ToUInt32(file, 0x44);
            var numDifat = BitConverter.ToUInt32(file, 0x48);

            // FAT 섹터 목록 (헤더의 DIFAT 109개 + 이어지는 DIFAT 섹터)
            var fatSectors = new List<uint>();
            for (var i = 0; i < 109 && fatSectors.Count < numFat; i++)
            {
                var s = BitConverter.ToUInt32(file, 0x4c + i * 4);
                if (s != 0xffffffff) fatSectors.Add(s);
            }
            var dif = difatStart;
            for (var n = 0; n < numDifat && dif is not (0xffffffff or 0xfffffffe); n++)
            {
                var b = Offset(dif);
                var per = _sectorSize / 4 - 1;
                for (var i = 0; i < per; i++)
                {
                    var s = BitConverter.ToUInt32(file, b + i * 4);
                    if (s != 0xffffffff) fatSectors.Add(s);
                }
                dif = BitConverter.ToUInt32(file, b + per * 4);
            }
            foreach (var s in fatSectors)
            {
                var b = Offset(s);
                for (var i = 0; i < _sectorSize / 4; i++) _fat.Add(BitConverter.ToUInt32(file, b + i * 4));
            }

            foreach (var s in Chain(miniFatStart))
            {
                var b = Offset(s);
                for (var i = 0; i < _sectorSize / 4; i++) _miniFat.Add(BitConverter.ToUInt32(file, b + i * 4));
            }

            // 디렉터리 엔트리
            uint rootStart = 0, rootSize = 0;
            foreach (var s in Chain(dirStart))
            {
                var b = Offset(s);
                for (var i = 0; i < _sectorSize / 128; i++)
                {
                    var off = b + i * 128;
                    var nameLen = BitConverter.ToUInt16(file, off + 0x40);
                    if (nameLen < 2) continue;
                    var name = Encoding.Unicode.GetString(file, off, nameLen - 2);
                    var type = file[off + 0x42];
                    var start = BitConverter.ToUInt32(file, off + 0x74);
                    var size = BitConverter.ToUInt32(file, off + 0x78);
                    if (type == 5) { rootStart = start; rootSize = size; }
                    else if (type == 2) _entries[name] = (start, size);
                }
            }
            _miniStream = rootSize > 0 ? ReadMain(rootStart, rootSize) : Array.Empty<byte>();
        }

        public byte[] Read(string name)
        {
            if (!_entries.TryGetValue(name, out var e)) return Array.Empty<byte>();
            if (e.Size >= _miniCutoff) return ReadMain(e.Start, e.Size);

            using var ms = new MemoryStream();
            var s = e.Start;
            var guard = 0;
            while (s is not (0xffffffff or 0xfffffffe) && guard++ < 100000)
            {
                var off = (int)s * _miniSectorSize;
                if (off + _miniSectorSize > _miniStream.Length) break;
                ms.Write(_miniStream, off, _miniSectorSize);
                s = s < _miniFat.Count ? _miniFat[(int)s] : 0xfffffffe;
            }
            return ms.ToArray().AsSpan(0, (int)Math.Min(e.Size, (uint)ms.Length)).ToArray();
        }

        private byte[] ReadMain(uint start, uint size)
        {
            using var ms = new MemoryStream();
            foreach (var s in Chain(start))
            {
                var off = Offset(s);
                if (off + _sectorSize > _file.Length) break;
                ms.Write(_file, off, _sectorSize);
            }
            return ms.ToArray().AsSpan(0, (int)Math.Min(size, (uint)ms.Length)).ToArray();
        }

        private int Offset(uint sector) => (int)((sector + 1) * (uint)_sectorSize);

        private IEnumerable<uint> Chain(uint start)
        {
            var s = start;
            var guard = 0;
            while (s is not (0xffffffff or 0xfffffffe) && guard++ < 200000)
            {
                yield return s;
                s = s < _fat.Count ? _fat[(int)s] : 0xfffffffe;
            }
        }
    }
}
