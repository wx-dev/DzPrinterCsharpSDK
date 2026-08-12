using System.Text.RegularExpressions;

namespace DzPrinter.Barcode;

/// <summary>
/// QR 码分段基类。对应 JS SDK 中 <c>Yt</c> 类。
/// 各模式分段（数字/字母数字/字节/汉字）均继承自本类。
/// </summary>
internal abstract class QrSegmentBase
{
    /// <summary>分段所属模式。对应 JS <c>Yt.mode</c> getter。</summary>
    public QrMode Mode { get; }

    /// <summary>分段原始数据字符串。对应 JS <c>Yt.data</c> getter。</summary>
    public string Data { get; }

    protected QrSegmentBase(QrMode mode, string data)
    {
        Mode = mode;
        Data = data ?? string.Empty;
    }

    /// <summary>获取分段数据长度。对应 JS <c>Yt.getLength()</c>，默认返回字符数。</summary>
    public virtual int GetLength() => Data.Length;

    /// <summary>获取分段编码后的位长度。对应 JS <c>Yt.getBitsLength()</c>，子类必须重写。</summary>
    public abstract int GetBitsLength();

    /// <summary>将分段编码写入位缓冲区。对应 JS <c>Yt.write(t)</c>，子类必须重写。</summary>
    public abstract void Write(BitBuffer buffer);
}

/// <summary>
/// 字母数字模式分段。对应 JS SDK 中 <c>Qt</c> 类。
/// 字符集：<c>0-9 A-Z $%*+-./:</c>（共 45 个），两两一组编码为 11 位，落单 6 位。
/// </summary>
internal sealed class QrAlphanumericSegment : QrSegmentBase
{
    /// <summary>
    /// 字母数字模式字符集。对应 JS <c>const Xt = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:".split("")</c>。
    /// 作为字符串存储，使用 <see cref="string.IndexOf(char)"/> 替代 JS 数组 <c>indexOf</c>。
    /// </summary>
    private const string Charset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    public QrAlphanumericSegment(string data) : base(QrMode.Alphanumeric, data) { }

    /// <summary>静态位长度计算。对应 JS <c>Qt.getBitsLength(t)</c>。</summary>
    public static int GetBitsLength(int length) => 11 * (length / 2) + length % 2 * 6;

    public override int GetBitsLength() => GetBitsLength(Data.Length);

    public override void Write(BitBuffer buffer)
    {
        int i;
        // 两两一组编码为 11 位
        for (i = 0; i + 2 <= Data.Length; i += 2)
        {
            var v = 45 * Charset.IndexOf(Data[i]) + Charset.IndexOf(Data[i + 1]);
            buffer.Put(v, 11);
        }
        // 落单字符编码为 6 位
        if ((Data.Length & 1) != 0)
            buffer.Put(Charset.IndexOf(Data[i]), 6);
    }
}

/// <summary>
/// 数字模式分段。对应 JS SDK 中 <c>te</c> 类。
/// 三位一组编码为 10 位，余 2 位编码为 7 位，余 1 位编码为 4 位。
/// </summary>
internal sealed class QrNumericSegment : QrSegmentBase
{
    public QrNumericSegment(string data) : base(QrMode.Numeric, data) { }

    /// <summary>静态位长度计算。对应 JS <c>te.getBitsLength(t)</c>。</summary>
    public static int GetBitsLength(int length)
    {
        // JS: 10*Math.floor(t/3) + (t%3 ? t%3*3+1 : 0)
        var r = length % 3;
        return 10 * (length / 3) + (r != 0 ? r * 3 + 1 : 0);
    }

    public override int GetBitsLength() => GetBitsLength(GetLength());

    public override void Write(BitBuffer buffer)
    {
        int i;
        string s;
        // 三位一组编码为 10 位
        for (i = 0; i + 3 <= Data.Length; i += 3)
        {
            s = Data.Substring(i, 3);  // JS: substr(i, 3)
            buffer.Put(int.Parse(s), 10);
        }
        // 余下的 1-2 位
        var n = Data.Length - i;
        if (n > 0)
        {
            s = Data.Substring(i);  // JS: substr(i)
            buffer.Put(int.Parse(s), 3 * n + 1);
        }
    }
}

/// <summary>
/// 字节模式分段。对应 JS SDK 中 <c>ee</c> 类。
/// 使用 UTF-8 编码，每个字节编码为 8 位。
/// </summary>
internal sealed class QrByteSegment : QrSegmentBase
{
    /// <summary>UTF-8 字节序列。对应 JS <c>ee.bytes = p.getBytes_Utf8(t)</c>。</summary>
    private readonly byte[] _bytes;

    public QrByteSegment(string data) : base(QrMode.Byte, data)
    {
        _bytes = TextEncodingUtils.GetBytesUtf8(data);
    }

    /// <summary>静态位长度计算。对应 JS <c>ee.getBitsLength(t)</c>。</summary>
    public static int GetBitsLength(int length) => 8 * length;

    public override int GetLength() => _bytes.Length;

    public override int GetBitsLength() => 8 * _bytes.Length;

    public override void Write(BitBuffer buffer)
    {
        for (var i = 0; i < _bytes.Length; i++)
            buffer.Put(_bytes[i], 8);
    }
}

/// <summary>
/// 汉字模式分段。对应 JS SDK 中 <c>ie</c> 类。
/// 使用 SJIS 编码值，每个字符编码为 13 位。
/// </summary>
internal sealed class QrKanjiSegment : QrSegmentBase
{
    public QrKanjiSegment(string data) : base(QrMode.Kanji, data) { }

    /// <summary>静态位长度计算。对应 JS <c>ie.getBitsLength(t)</c>。</summary>
    public static int GetBitsLength(int length) => 13 * length;

    public override int GetBitsLength() => GetBitsLength(GetLength());

    public override void Write(BitBuffer buffer)
    {
        for (var i = 0; i < Data.Length; i++)
        {
            // JS: wt.toSJISFunction ? wt.toSJISFunction(this.mData[e]) : this.mData.charCodeAt(e)
            var v = QrSymbolUtils.ToSjisFunction != null
                ? QrSymbolUtils.ToSjisFunction!(Data[i])
                : (int)Data[i];

            if (v >= 33088 && v <= 40956)
            {
                v -= 33088;
            }
            else if (v >= 57408 && v <= 60351)
            {
                v -= 49472;
            }
            else
            {
                throw new InvalidOperationException(
                    "Invalid SJIS character: " + Data[i] + "\nMake sure your charset is UTF-8");
            }

            // JS: i = 192 * (i >>> 8 & 255) + (255 & i)
            v = 192 * ((v >> 8) & 0xFF) + (v & 0xFF);
            buffer.Put(v, 13);
        }
    }
}

/// <summary>
/// 分段信息数据类。对应 JS 中 <c>{data, mode, length, index}</c> 对象。
/// 用于在 <see cref="QrSegmentBuilder"/> 流水线中传递分段元信息。
/// </summary>
internal sealed class QrSegmentInfo
{
    public string Data { get; set; } = string.Empty;
    public QrMode Mode { get; set; } = null!;
    public int Length { get; set; }
    public int Index { get; set; }
}

/// <summary>
/// 图节点表项。对应 JS <c>buildGraph</c> 中 <c>i[h] = {node, lastCount}</c> 对象。
/// </summary>
internal sealed class GraphNodeEntry
{
    public QrSegmentInfo Node { get; set; } = null!;
    public int LastCount { get; set; }
}

/// <summary>
/// 简单优先队列。对应 JS SDK 中 <c>se</c> 对象。
/// JS 实现每次 push 后对整个队列排序，pop 取队首；C# 同样以 List + Sort 实现，保持保真。
/// </summary>
internal sealed class PriorityQueue<T>
{
    private readonly List<Item> _queue = new();
    private readonly Func<int, int, int> _sorter;

    private PriorityQueue(Func<int, int, int> sorter) => _sorter = sorter;

    /// <summary>
    /// 工厂方法。对应 JS <c>se.make(t)</c>。
    /// 默认排序器按 cost 升序（对应 JS <c>se.default_sorter = (t, e) => t.cost - e.cost</c>）。
    /// </summary>
    public static PriorityQueue<T> Make(Func<int, int, int>? sorter = null) =>
        new(sorter ?? ((a, b) => a - b));

    /// <summary>入队。对应 JS <c>se.push(t, e)</c>。</summary>
    public void Push(T value, int cost)
    {
        _queue.Add(new Item { Value = value, Cost = cost });
        _queue.Sort((a, b) => _sorter(a.Cost, b.Cost));
    }

    /// <summary>出队（取队首）。对应 JS <c>se.pop()</c>。</summary>
    public Item? Pop()
    {
        if (_queue.Count == 0) return null;
        var first = _queue[0];
        _queue.RemoveAt(0);
        return first;
    }

    /// <summary>队列是否为空。对应 JS <c>se.empty()</c>。</summary>
    public bool Empty() => _queue.Count == 0;

    public sealed class Item
    {
        public T Value { get; set; } = default!;
        public int Cost { get; set; }
    }
}

/// <summary>
/// Dijkstra 最短路径算法。对应 JS SDK 中 <c>ne</c> 对象。
/// 用于 QR 码分段模式选择优化（求最小位数的模式组合）。
/// </summary>
internal static class Dijkstra
{
    /// <summary>
    /// 单源最短路径前驱表。对应 JS <c>ne.single_source_shortest_paths(t, e, i)</c>。
    /// </summary>
    /// <param name="graph">邻接表：节点 → (邻居 → 边权)。</param>
    /// <param name="source">起点。</param>
    /// <param name="target">终点（仅用于错误检查；若非空且不可达则抛异常）。</param>
    /// <returns>前驱字典：节点 → 前驱节点。</returns>
    public static Dictionary<string, string> SingleSourceShortestPaths(
        Dictionary<string, Dictionary<string, int>> graph, string source, string? target)
    {
        var predecessors = new Dictionary<string, string>();
        var distances = new Dictionary<string, int> { [source] = 0 };
        var queue = PriorityQueue<string>.Make();

        queue.Push(source, 0);
        while (!queue.Empty())
        {
            var a = queue.Pop()!;
            var o = a.Value;
            var h = a.Cost;

            // JS: d = t[o] || {}
            if (!graph.TryGetValue(o, out var d)) d = new Dictionary<string, int>();

            foreach (var pair in d)
            {
                var c = pair.Key;
                var u = pair.Value;
                var l = h + u;
                // p = void 0 === n[c]（c 尚未访问）；g = n[c]
                var p = !distances.TryGetValue(c, out var g);
                if (p || g > l)
                {
                    distances[c] = l;
                    queue.Push(c, l);
                    predecessors[c] = o;
                }
            }
        }

        if (target != null && !distances.ContainsKey(target))
            throw new InvalidOperationException(
                "Could not find a path from " + source + " to " + target + ".");

        return predecessors;
    }

    /// <summary>
    /// 从前驱表回溯最短路径。对应 JS <c>ne.extract_shortest_path_from_predecessor_list(t, e)</c>。
    /// </summary>
    public static List<string> ExtractShortestPath(Dictionary<string, string> predecessors, string target)
    {
        var path = new List<string>();
        var s = target;
        // JS: for (; s;) i.push(s), t[s], s = t[s];
        while (s != null)
        {
            path.Add(s);
            // t[s] 可能不存在（终点 end 没有前驱）；JS 中 `t[s]` 求值为 undefined，赋值给 s 后退出循环
            s = predecessors.TryGetValue(s, out var pred) ? pred : null!;
        }
        path.Reverse();
        return path;
    }

    /// <summary>
    /// 查找从 source 到 target 的最短路径。对应 JS <c>ne.find_path(t, e, i)</c>。
    /// </summary>
    public static List<string> FindPath(
        Dictionary<string, Dictionary<string, int>> graph, string source, string target)
    {
        var predecessors = SingleSourceShortestPaths(graph, source, target);
        return ExtractShortestPath(predecessors, target);
    }
}

/// <summary>
/// QR 码分段构造器。对应 JS SDK 中 <c>re</c> 类。
/// 提供：从字符串提取分段、构建模式选择图、用 Dijkstra 求最优分段组合。
/// </summary>
internal static class QrSegmentBuilder
{
    /// <summary>
    /// 获取字符串的 UTF-8 字节长度。对应 JS <c>re.getStringByteLength(t)</c>。
    /// JS 实现 <c>unescape(encodeURIComponent(t)).length</c> 等价于 UTF-8 字节长度。
    /// </summary>
    public static int GetStringByteLength(string text) => TextEncodingUtils.GetBytesUtf8(text).Length;

    /// <summary>
    /// 用正则提取所有匹配段。对应 JS <c>re.getSegments(t, e, i)</c>。
    /// </summary>
    /// <param name="regex">全局匹配正则（C# 中等价于 <see cref="Regex.Matches(string)"/>）。</param>
    /// <param name="mode">匹配段所属模式。</param>
    /// <param name="text">输入字符串。</param>
    public static List<QrSegmentInfo> GetSegments(Regex regex, QrMode mode, string text)
    {
        var result = new List<QrSegmentInfo>();
        foreach (Match m in regex.Matches(text))
        {
            result.Add(new QrSegmentInfo
            {
                Data = m.Value,
                Index = m.Index,
                Mode = mode,
                Length = m.Value.Length
            });
        }
        return result;
    }

    /// <summary>
    /// 从字符串提取所有模式的分段。对应 JS <c>re.getSegmentsFromString(t)</c>。
    /// 按 NUMERIC/ALPHANUMERIC/BYTE/KANJI 四种模式各跑一遍正则，合并后按索引排序，最后将 index 重置为 0。
    /// </summary>
    public static List<QrSegmentInfo> GetSegmentsFromString(string text)
    {
        var numeric = GetSegments(QrCharMode.NumericSegmentsRegex, QrMode.Numeric, text);
        var alphanumeric = GetSegments(QrCharMode.AlphanumericSegmentsRegex, QrMode.Alphanumeric, text);

        List<QrSegmentInfo> byteSegs;
        List<QrSegmentInfo> kanjiSegs;
        if (QrSymbolUtils.ToSjisFunction != null)
        {
            byteSegs = GetSegments(QrCharMode.ByteSegmentsRegex, QrMode.Byte, text);
            kanjiSegs = GetSegments(QrCharMode.KanjiSegmentsRegex, QrMode.Kanji, text);
        }
        else
        {
            // 无 SJIS 钩子时使用 BYTE_KANJI（含汉字的字节模式），KANJI 段为空
            byteSegs = GetSegments(QrCharMode.ByteKanjiSegmentsRegex, QrMode.Byte, text);
            kanjiSegs = new List<QrSegmentInfo>();
        }

        var combined = new List<QrSegmentInfo>(numeric.Count + alphanumeric.Count + byteSegs.Count + kanjiSegs.Count);
        combined.AddRange(numeric);
        combined.AddRange(alphanumeric);
        combined.AddRange(byteSegs);
        combined.AddRange(kanjiSegs);
        combined.Sort((a, b) => a.Index - b.Index);

        // JS: .map(t => ({data, mode, length, index: 0}))
        foreach (var seg in combined) seg.Index = 0;
        return combined;
    }

    /// <summary>
    /// 获取指定模式与长度的位长度。对应 JS <c>re.getSegmentBitsLength(t, e)</c>。
    /// </summary>
    public static int GetSegmentBitsLength(int length, QrMode mode)
    {
        if (ReferenceEquals(mode, QrMode.Numeric)) return QrNumericSegment.GetBitsLength(length);
        if (ReferenceEquals(mode, QrMode.Alphanumeric)) return QrAlphanumericSegment.GetBitsLength(length);
        if (ReferenceEquals(mode, QrMode.Kanji)) return QrKanjiSegment.GetBitsLength(length);
        // BYTE / default
        return QrByteSegment.GetBitsLength(length);
    }

    /// <summary>
    /// 合并相同模式的相邻分段。对应 JS <c>re.mergeSegments(t)</c>。
    /// </summary>
    public static List<QrSegmentInfo> MergeSegments(List<QrSegmentInfo> segments)
    {
        var result = new List<QrSegmentInfo>();
        foreach (var seg in segments)
        {
            var last = result.Count - 1 >= 0 ? result[result.Count - 1] : null;
            if (last != null && ReferenceEquals(last.Mode, seg.Mode))
            {
                // 合并：拼接 data
                last.Data += seg.Data;
            }
            else
            {
                result.Add(new QrSegmentInfo
                {
                    Data = seg.Data,
                    Mode = seg.Mode,
                    Length = seg.Length,
                    Index = seg.Index
                });
            }
        }
        return result;
    }

    /// <summary>
    /// 为每个分段构建模式转换候选列表。对应 JS <c>re.buildNodes(t)</c>。
    /// 每个分段可保持原模式或降级为更宽松的模式（NUMERIC→ALPHANUMERIC→BYTE，KANJI→BYTE）。
    /// </summary>
    public static List<List<QrSegmentInfo>> BuildNodes(List<QrSegmentInfo> segments)
    {
        var result = new List<List<QrSegmentInfo>>();
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var list = new List<QrSegmentInfo>();
            if (ReferenceEquals(s.Mode, QrMode.Numeric))
            {
                list.Add(s);
                list.Add(new QrSegmentInfo { Data = s.Data, Mode = QrMode.Alphanumeric, Length = s.Length });
                list.Add(new QrSegmentInfo { Data = s.Data, Mode = QrMode.Byte, Length = s.Length });
            }
            else if (ReferenceEquals(s.Mode, QrMode.Alphanumeric))
            {
                list.Add(s);
                list.Add(new QrSegmentInfo { Data = s.Data, Mode = QrMode.Byte, Length = s.Length });
            }
            else if (ReferenceEquals(s.Mode, QrMode.Kanji))
            {
                list.Add(s);
                list.Add(new QrSegmentInfo { Data = s.Data, Mode = QrMode.Byte, Length = GetStringByteLength(s.Data) });
            }
            else // BYTE
            {
                list.Add(new QrSegmentInfo { Data = s.Data, Mode = QrMode.Byte, Length = GetStringByteLength(s.Data) });
            }
            result.Add(list);
        }
        return result;
    }

    /// <summary>
    /// 构建模式选择图。对应 JS <c>re.buildGraph(t, e)</c>。
    /// 图中每条边表示一种模式选择方案，边权为该方案增加的位数。
    /// 使用 Dijkstra 算法求从 start 到 end 的最短路径即可得到最小位数组合。
    /// </summary>
    /// <param name="nodes">每个分段的模式候选列表（来自 <see cref="BuildNodes"/>）。</param>
    /// <param name="version">QR 码版本（用于计算字符计数指示位数）。</param>
    public static (Dictionary<string, Dictionary<string, int>> Map,
                  Dictionary<string, GraphNodeEntry> Table) BuildGraph(
        List<List<QrSegmentInfo>> nodes, int version)
    {
        var table = new Dictionary<string, GraphNodeEntry>();
        var map = new Dictionary<string, Dictionary<string, int>>();
        var startKey = "start";
        map[startKey] = new Dictionary<string, int>();

        var prevKeys = new List<string> { startKey };

        for (var r = 0; r < nodes.Count; r++)
        {
            var segmentList = nodes[r];
            var currentKeys = new List<string>();
            for (var t = 0; t < segmentList.Count; t++)
            {
                var c = segmentList[t];
                var key = "" + r + t;  // JS: h = "" + r + t
                currentKeys.Add(key);
                table[key] = new GraphNodeEntry { Node = c, LastCount = 0 };
                map[key] = new Dictionary<string, int>();

                foreach (var prevKey in prevKeys)
                {
                    if (table.TryGetValue(prevKey, out var prevEntry) &&
                        ReferenceEquals(prevEntry.Node.Mode, c.Mode))
                    {
                        // 同模式：累加长度，边权为增量位数
                        // JS: s[r][h] = getSegmentBitsLength(i[r].lastCount + c.length, c.mode) - getSegmentBitsLength(i[r].lastCount, c.mode)
                        map[prevKey][key] = GetSegmentBitsLength(prevEntry.LastCount + c.Length, c.Mode)
                                          - GetSegmentBitsLength(prevEntry.LastCount, c.Mode);
                        prevEntry.LastCount += c.Length;
                    }
                    else
                    {
                        // 不同模式：重置 lastCount，边权包含模式指示 + 字符计数指示
                        if (table.TryGetValue(prevKey, out var pe))
                            pe.LastCount = c.Length;
                        // JS: s[r][h] = getSegmentBitsLength(c.length, c.mode) + 4 + Vt.getCharCountIndicator(c.mode, e)
                        map[prevKey][key] = GetSegmentBitsLength(c.Length, c.Mode)
                                          + 4 + QrModeUtils.GetCharCountIndicator(c.Mode, version);
                    }
                }
            }
            prevKeys = currentKeys;
        }

        // 末尾所有节点连接到 end，边权 0
        foreach (var key in prevKeys)
        {
            if (!map.ContainsKey(key)) map[key] = new Dictionary<string, int>();
            map[key]["end"] = 0;
        }

        return (map, table);
    }

    /// <summary>
    /// 构建单段（无模式优化）。对应 JS <c>re.buildSingleSegment(t, e)</c>。
    /// </summary>
    /// <param name="text">输入文本。</param>
    /// <param name="preferredMode">优先模式（字符串或 QrMode）；为空则使用最佳模式。</param>
    public static QrSegmentBase BuildSingleSegment(string text, object? preferredMode = null)
    {
        var best = QrModeUtils.GetBestModeForData(text);
        // JS: i = Vt.from(e || "", s)
        var mode = QrModeUtils.From(preferredMode ?? string.Empty, best);

        // JS: if (!i || i !== Vt.BYTE && (null == i ? void 0 : i.bit) < s.bit) throw ...
        if (mode == null || (!ReferenceEquals(mode, QrMode.Byte) && mode.Bit < best.Bit))
            throw new InvalidOperationException(
                "\"" + text + "\" cannot be encoded with mode " + QrModeUtils.ToString(mode) +
                ".\n Suggested mode is: " + QrModeUtils.ToString(best));

        // JS: i === Vt.KANJI && "function" != typeof wt.toSJISFunction && (i = Vt.BYTE)
        if (ReferenceEquals(mode, QrMode.Kanji) && QrSymbolUtils.ToSjisFunction == null)
            mode = QrMode.Byte;

        if (ReferenceEquals(mode, QrMode.Numeric)) return new QrNumericSegment(text);
        if (ReferenceEquals(mode, QrMode.Alphanumeric)) return new QrAlphanumericSegment(text);
        if (ReferenceEquals(mode, QrMode.Kanji)) return new QrKanjiSegment(text);
        // BYTE
        return new QrByteSegment(text);
    }

    /// <summary>
    /// 从段信息数组构建段对象列表。对应 JS <c>re.fromArray(t)</c>。
    /// 数组元素可为字符串或 <see cref="QrSegmentInfo"/>（含 data/mode）。
    /// </summary>
    public static List<QrSegmentBase> FromArray(IEnumerable<object> array)
    {
        var result = new List<QrSegmentBase>();
        foreach (var item in array)
        {
            if (item is string s)
            {
                var seg = BuildSingleSegment(s);
                if (seg != null) result.Add(seg);
            }
            else if (item is QrSegmentInfo info)
            {
                var seg = BuildSingleSegment(info.Data, info.Mode);
                if (seg != null) result.Add(seg);
            }
        }
        return result;
    }

    /// <summary>
    /// 从字符串构建段列表（含模式优化）。对应 JS <c>re.fromString(t, e)</c>。
    /// </summary>
    /// <param name="text">输入文本。</param>
    /// <param name="version">QR 码版本（用于字符计数指示位数计算）。</param>
    public static List<QrSegmentBase> FromString(string text, int version)
    {
        var segments = GetSegmentsFromString(text);
        var nodes = BuildNodes(segments);
        var (map, table) = BuildGraph(nodes, version);
        var path = Dijkstra.FindPath(map, "start", "end");

        // 路径形如 ["start", "00", "11", ..., "end"]，去掉首尾
        var arr = new List<QrSegmentInfo>();
        for (var i = 1; i < path.Count - 1; i++)
            arr.Add(table[path[i]].Node);

        return FromArray(MergeSegments(arr));
    }

    /// <summary>
    /// 直接分段（无模式优化）。对应 JS <c>re.rawSplit(t)</c>。
    /// 仅按各模式正则切分，不做模式转换图搜索。
    /// </summary>
    public static List<QrSegmentBase> RawSplit(string text) =>
        FromArray(GetSegmentsFromString(text));
}
