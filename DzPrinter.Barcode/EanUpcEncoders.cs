using System.Text.RegularExpressions;

namespace DzPrinter.Barcode;

/// <summary>
/// EAN-2（2 位附加码）编码器。对应 JS SDK 中 <c>U</c> 类。
/// </summary>
internal sealed class Ean2Encoder : Barcode1DEncoder
{
    public Ean2Encoder(string data, BarcodeEncodeOptions options) : base(data, options) { }

    public override bool Valid() => Regex.IsMatch(Data, @"^[0-9]{2}$");

    public override BarcodeEncodeResult Encode()
    {
        var structure = EanUpcTables.Ean2Structure[int.Parse(Data) % 4];
        var data = "1011" + EanUpcTables.EncodeDigits(Data, structure, "01");
        return new BarcodeEncodeResult
        {
            Options = Options,
            Items = { new BarcodeItem(data, Text) },
            Text = Text
        };
    }
}

/// <summary>
/// EAN-5（5 位附加码）编码器。对应 JS SDK 中 <c>F</c> 类。
/// </summary>
internal sealed class Ean5Encoder : Barcode1DEncoder
{
    /// <summary>
    /// EAN-5 校验和。对应 JS <c>F.checksum(t)</c>。
    /// 算法：奇数位 ×9 + 偶数位 ×3，加和 mod 10。
    /// </summary>
    public static int Checksum(string text)
    {
        var sum = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var d = text[i] - '0';
            sum += (i % 2 != 0) ? 9 * d : 3 * d;
        }
        return sum % 10;
    }

    public Ean5Encoder(string data, BarcodeEncodeOptions options) : base(data, options) { }

    public override bool Valid() => Regex.IsMatch(Data, @"^[0-9]{5}$");

    public override BarcodeEncodeResult Encode()
    {
        var structure = EanUpcTables.Ean5Structure[Checksum(Data)];
        return new BarcodeEncodeResult
        {
            Items = { new BarcodeItem("1011" + EanUpcTables.EncodeDigits(Data, structure, "01"), Text) },
            Text = Text,
            Options = Options
        };
    }
}

/// <summary>
/// EAN-8 编码器。对应 JS SDK 中 <c>$</c> 类（<c>class $ extends N</c>）。
/// </summary>
internal sealed class Ean8Encoder : EanUpcEncoderBase
{
    /// <summary>
    /// EAN-8 校验位。对应 JS <c>$.checksum(t)</c>。
    /// 算法：前 7 位，奇数位 ×3 + 偶数位 ×1，加和取模 10 后用 10 减。
    /// </summary>
    public static int Checksum(string text)
    {
        var sum = 0;
        for (var i = 0; i < 7; i++)
        {
            var d = text[i] - '0';
            sum += (i % 2 != 0) ? d : 3 * d;
        }
        return (10 - sum % 10) % 10;
    }

    public Ean8Encoder(string data, BarcodeEncodeOptions options) : base(Normalize(data), options) { }

    private static string Normalize(string data)
    {
        if (Regex.IsMatch(data, @"^[0-9]{7}$")) data += Checksum(data);
        return data;
    }

    public override bool Valid() =>
        Regex.IsMatch(Data, @"^[0-9]{8}$") && (Data[7] - '0') == Checksum(Data);

    protected override string LeftText() => Text.Substring(0, 4);
    protected override string LeftEncode() => EanUpcTables.EncodeDigits(Data.Substring(0, 4), "LLLL");
    protected override string RightText() => Text.Substring(4, 4);
    protected override string RightEncode() => EanUpcTables.EncodeDigits(Data.Substring(4, 4), "RRRR");
}

/// <summary>
/// EAN-13 编码器。对应 JS SDK 中 <c>j</c> 类（<c>let j = class t extends N</c>）。
/// </summary>
internal sealed class Ean13Encoder : EanUpcEncoderBase
{
    /// <summary>
    /// EAN-13 校验位。对应 JS <c>j.checksum(t)</c>。
    /// 算法：前 12 位，奇数位 ×3 + 偶数位 ×1，加和取模 10 后用 10 减。
    /// </summary>
    public static int Checksum(string text)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var d = text[i] - '0';
            sum += (i % 2 != 0) ? 3 * d : d;
        }
        return (10 - sum % 10) % 10;
    }

    public Ean13Encoder(string data, BarcodeEncodeOptions options) : base(Normalize(data), options) { }

    private static string Normalize(string data)
    {
        if (Regex.IsMatch(data, @"^[0-9]{12}$")) data += Checksum(data);
        return data;
    }

    public override bool Valid() =>
        Regex.IsMatch(Data, @"^[0-9]{13}$") && (Data[12] - '0') == Checksum(Data);

    protected override string LeftText() => Text.Substring(1, 6);
    protected override string LeftEncode()
    {
        var digits = Data.Substring(1, 6);
        var structure = EanUpcTables.Ean13Structure[Data[0] - '0'];
        return EanUpcTables.EncodeDigits(digits, structure);
    }

    protected override string RightText() => Text.Substring(7, 6);
    protected override string RightEncode() => EanUpcTables.EncodeDigits(Data.Substring(7, 6), "RRRRRR");

    /// <summary>
    /// EAN-13 守护式编码重写：在首位前增加显示文本。对应 JS <c>j.encodeGuarded()</c>。
    /// </summary>
    protected override List<BarcodeItem> EncodeGuarded()
    {
        var items = base.EncodeGuarded();
        if (QuietZones > 0)
        {
            // JS: t[0].text = this.text[0] —— 将首段（静区）的文本设为第一位数字
            items[0] = new BarcodeItem(items[0].Data, Text.Length > 0 ? Text[0].ToString() : string.Empty);
        }
        else
        {
            // 无静区时插入一段仅含首位数字的项
            items.Insert(0, new BarcodeItem("00000", Text.Length > 0 ? Text[0].ToString() : string.Empty));
        }
        return items;
    }
}

/// <summary>
/// UPC-A 编码器。对应 JS SDK 中 <c>W</c> 类。
/// </summary>
internal sealed class UpcAEncoder : Barcode1DEncoder
{
    /// <summary>
    /// UPC-A 校验位。对应 JS <c>W.checksum(t)</c>。
    /// 算法：11 位，奇数位 ×3 + 偶数位 ×1，加和取模 10 后用 10 减。
    /// 注意 JS 中索引从 0 开始，e=1,3,5,7,9 是奇数位（×1），e=0,2,4,6,8,10 是偶数位（×3）。
    /// </summary>
    public static int Checksum(string text)
    {
        var i = 0;
        // e=1,3,5,7,9：sum += parseInt(t[e])
        for (var e = 1; e < 11; e += 2) i += text[e] - '0';
        // e=0,2,4,6,8,10：sum += 3 * parseInt(t[e])
        for (var e = 0; e < 11; e += 2) i += 3 * (text[e] - '0');
        return (10 - i % 10) % 10;
    }

    public UpcAEncoder(string data, BarcodeEncodeOptions options) : base(Normalize(data), options) { }

    private static string Normalize(string data)
    {
        if (Regex.IsMatch(data, @"^[0-9]{11}$")) data += Checksum(data);
        return data;
    }

    public override bool Valid() =>
        Regex.IsMatch(Data, @"^[0-9]{12}$") && (Data[11] - '0') == Checksum(Data);

    public override BarcodeEncodeResult Encode()
    {
        var items = Options.Flat ? FlatEncoding() : GuardedEncoding();
        return new BarcodeEncodeResult
        {
            Items = items,
            Text = Text,
            Options = Options
        };
    }

    /// <summary>
    /// 扁平式编码。对应 JS <c>W.flatEncoding()</c>。
    /// </summary>
    private List<BarcodeItem> FlatEncoding()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("101");
        sb.Append(EanUpcTables.EncodeDigits(Data.Substring(0, 6), "LLLLLL"));
        sb.Append("01010");
        // JS Bug: this.data.substring(6,6) —— 第二个参数应为 12，但写成了 6，
        // 导致子串为空，右侧 6 位数字不会被编码。保留 JS 行为以保真。
        sb.Append(EanUpcTables.EncodeDigits(Data.Substring(6, 6), "RRRRRR"));
        sb.Append("101");
        return new List<BarcodeItem> { new(sb.ToString(), Text) };
    }

    /// <summary>
    /// 守护式编码。对应 JS <c>W.guardedEncoding()</c>。
    /// </summary>
    private List<BarcodeItem> GuardedEncoding()
    {
        var items = new List<BarcodeItem>();
        var quietWidth = Options.QuietZones is int q && q > 2 ? q : 2;
        var quiet = new string('0', quietWidth);
        items.Add(new BarcodeItem(quiet, Text.Substring(0, 1)));
        items.Add(new BarcodeItem("101" + EanUpcTables.EncodeDigits(Data[0].ToString(), "L"), string.Empty));
        items.Add(new BarcodeItem(EanUpcTables.EncodeDigits(Data.Substring(1, 5), "LLLLL"), Text.Substring(1, 5)));
        items.Add(new BarcodeItem("01010", string.Empty));
        items.Add(new BarcodeItem(EanUpcTables.EncodeDigits(Data.Substring(6, 5), "RRRRR"), Text.Substring(6, 5)));
        items.Add(new BarcodeItem(EanUpcTables.EncodeDigits(Data[11].ToString(), "R") + "101", string.Empty));
        items.Add(new BarcodeItem(quiet, Text.Substring(11, 1)));
        return items;
    }
}

/// <summary>
/// UPC-E 编码器。对应 JS SDK 中 <c>J</c> 类。
/// UPC-E 是 UPC-A 的压缩形式，仅用于数字系统为 0 或 1 的条码。
/// </summary>
internal sealed class UpcEEncoder : Barcode1DEncoder
{
    /// <summary>
    /// 将 UPC-E 展开为 UPC-A。对应 JS <c>J.expandToUPCA(t, e)</c>。
    /// </summary>
    /// <param name="t">UPC-E 中间 6 位数字。</param>
    /// <param name="e">数字系统（首位，0 或 1）。</param>
    public static string ExpandToUpcA(string middleDigits, string numberSystem)
    {
        var lastDigit = middleDigits[middleDigits.Length - 1] - '0';
        var template = EanUpcTables.UpcENumberSystem[lastDigit];
        var sb = new System.Text.StringBuilder();
        var idx = 0;
        for (var i = 0; i < template.Length; i++)
        {
            sb.Append(template[i] == 'X' ? middleDigits[idx++] : template[i]);
        }
        var upcA = numberSystem + sb.ToString();
        return upcA + UpcAEncoder.Checksum(upcA);
    }

    private readonly string _middleDigits;
    private readonly string _upcA;
    private readonly bool _isValid;

    public UpcEEncoder(string data, BarcodeEncodeOptions options) : base(data, options)
    {
        _middleDigits = string.Empty;
        _upcA = string.Empty;
        _isValid = false;

        if (Regex.IsMatch(data, @"^[0-9]{6}$"))
        {
            _middleDigits = data;
            _upcA = ExpandToUpcA(data, "0");
            Text = options?.Text ?? $"{_upcA[0]}{data}{_upcA[_upcA.Length - 1]}";
            _isValid = true;
        }
        else if (Regex.IsMatch(data, @"^[01][0-9]{7}$"))
        {
            _middleDigits = data.Substring(1, data.Length - 2);
            _upcA = ExpandToUpcA(_middleDigits, data[0].ToString());
            if (_upcA[_upcA.Length - 1] != data[data.Length - 1]) return;
            _isValid = true;
        }
    }

    public override bool Valid() => _isValid;

    public override BarcodeEncodeResult Encode()
    {
        var items = Options.Flat ? FlatEncoding() : GuardedEncoding();
        return new BarcodeEncodeResult
        {
            Items = items,
            Text = Text,
            Options = Options
        };
    }

    private List<BarcodeItem> FlatEncoding()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("101");
        sb.Append(EncodeMiddleDigits());
        sb.Append("010101");
        return new List<BarcodeItem> { new(sb.ToString(), Text) };
    }

    private List<BarcodeItem> GuardedEncoding()
    {
        var items = new List<BarcodeItem>();
        var quietWidth = Options.QuietZones is int q && q > 2 ? q : 2;
        var quiet = new string('0', quietWidth);
        items.Add(new BarcodeItem(quiet, Text.Length > 0 ? Text[0].ToString() : string.Empty));
        items.Add(new BarcodeItem("101", string.Empty));
        items.Add(new BarcodeItem(EncodeMiddleDigits(), Text.Length >= 7 ? Text.Substring(1, 6) : Text));
        items.Add(new BarcodeItem("010101", string.Empty));
        items.Add(new BarcodeItem(quiet, Text.Length >= 8 ? Text[7].ToString() : string.Empty));
        return items;
    }

    /// <summary>
    /// 编码 UPC-E 中间 6 位数字。对应 JS <c>J.encodeMiddleDigits()</c>。
    /// </summary>
    private string EncodeMiddleDigits()
    {
        var first = _upcA[0] - '0';   // 数字系统
        var last = _upcA[_upcA.Length - 1] - '0';  // 校验位
        var structure = EanUpcTables.UpcEParityStructure[last][first];
        return EanUpcTables.EncodeDigits(_middleDigits, structure);
    }
}
