namespace DzPrinter.Barcode;

/// <summary>
/// GF(256) 伽罗瓦域运算。对应 JS SDK 中 <c>zt</c> 类。
/// 使用多项式 0x11D (=285) 作为生成元，查表实现 log/exp/mul。
/// </summary>
internal static class QrGaloisField
{
    /// <summary>
    /// 对数运算。对应 JS <c>zt.log(t)</c>。
    /// 注意：0 没有对数，调用方需保证 t ≥ 1。
    /// </summary>
    public static int Log(int value)
    {
        if (value < 1) throw new ArgumentException("log(" + value + ")");
        return QrTables.GfLog[value];
    }

    /// <summary>
    /// 指数运算。对应 JS <c>zt.exp(t)</c>。
    /// 索引 0-254 返回 α^i，255-511 镜像返回 α^(i-255)。
    /// </summary>
    public static int Exp(int exponent) => QrTables.GfExp[exponent];

    /// <summary>
    /// GF(256) 乘法。对应 JS <c>zt.mul(t, e)</c>。
    /// 任一为 0 直接返回 0；否则通过 log/exp 表查表相加。
    /// </summary>
    public static int Mul(int a, int b)
    {
        if (a == 0 || b == 0) return 0;
        return QrTables.GfExp[QrTables.GfLog[a] + QrTables.GfLog[b]];
    }
}

/// <summary>
/// GF(256) 上的多项式运算。对应 JS SDK 中 <c>qt</c> 类。
/// 多项式以 byte[] 表示，索引 0 为最高次系数。
/// </summary>
internal static class QrPolynomial
{
    /// <summary>
    /// 多项式乘法。对应 JS <c>qt.mul(t, e)</c>。
    /// 返回长度为 <c>t.length + e.length - 1</c> 的新数组。
    /// </summary>
    public static byte[] Mul(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length - 1];
        for (var i = 0; i < a.Length; i++)
            for (var j = 0; j < b.Length; j++)
                result[i + j] ^= (byte)QrGaloisField.Mul(a[i], b[j]);
        return result;
    }

    /// <summary>
    /// 多项式取模。对应 JS <c>qt.mod(t, e)</c>。
    /// 通过反复消除最高次项实现多项式除法的余数计算。
    /// </summary>
    public static byte[] Mod(byte[] dividend, byte[] divisor)
    {
        var result = (byte[])dividend.Clone();
        while (result.Length - divisor.Length >= 0)
        {
            var coef = result[0];
            for (var i = 0; i < divisor.Length; i++)
                result[i] ^= (byte)QrGaloisField.Mul(divisor[i], coef);

            // 去除前导 0
            var skip = 0;
            while (skip < result.Length && result[skip] == 0) skip++;
            // JS: i = i.slice(s) —— 若全为 0 则 slice 返回空数组
            var trimmed = new byte[result.Length - skip];
            Array.Copy(result, skip, trimmed, 0, trimmed.Length);
            result = trimmed;
        }
        return result;
    }

    /// <summary>
    /// 生成次数为 degree 的 RS 纠错多项式。对应 JS <c>qt.generateECPolynomial(t)</c>。
    /// 算法：累乘 (x - α^0)(x - α^1)...(x - α^(degree-1))，初始为 [1]。
    /// </summary>
    public static byte[] GenerateEcPolynomial(int degree)
    {
        var result = new byte[] { 1 };
        for (var i = 0; i < degree; i++)
            result = Mul(result, new byte[] { 1, (byte)QrGaloisField.Exp(i) });
        return result;
    }
}

/// <summary>
/// Reed-Solomon 纠错编码器。对应 JS SDK 中 <c>Zt</c> 类。
/// 持有一个生成多项式，可对多段数据反复编码。
/// </summary>
internal sealed class QrReedSolomonEncoder
{
    /// <summary>纠错次数（亦即生成多项式次数）。对应 JS <c>Zt.degree</c>。</summary>
    public int Degree { get; private set; }

    /// <summary>生成多项式。对应 JS <c>Zt.genPoly</c>。</summary>
    private byte[]? _genPoly;

    /// <summary>
    /// 构造编码器。对应 JS <c>new Zt(t)</c>。
    /// degree ≠ 0 时立即调用 <see cref="Initialize"/>。
    /// </summary>
    public QrReedSolomonEncoder(int degree)
    {
        Degree = degree;
        if (degree != 0) Initialize(degree);
    }

    /// <summary>
    /// 初始化生成多项式。对应 JS <c>Zt.initialize(t)</c>。
    /// </summary>
    public void Initialize(int degree)
    {
        Degree = degree;
        _genPoly = QrPolynomial.GenerateEcPolynomial(degree);
    }

    /// <summary>
    /// 对数据进行 RS 编码，返回长度为 <see cref="Degree"/> 的纠错码字。
    /// 对应 JS <c>Zt.encode(t)</c>。
    /// 算法：数据后补 degree 个 0 → 对生成多项式取模 → 左侧补 0 对齐 degree 长度。
    /// </summary>
    public byte[] Encode(byte[] data)
    {
        if (_genPoly == null) throw new InvalidOperationException("Encoder not initialized");

        // e = new Uint8Array(t.length + this.degree); e.set(t);
        var padded = new byte[data.Length + Degree];
        Array.Copy(data, 0, padded, 0, data.Length);

        // i = qt.mod(e, this.genPoly)
        var remainder = QrPolynomial.Mod(padded, _genPoly);

        // s = this.degree - i.length; if (s > 0) { 前置补 0 }
        var s = Degree - remainder.Length;
        if (s > 0)
        {
            var result = new byte[Degree];
            Array.Copy(remainder, 0, result, s, remainder.Length);
            return result;
        }
        return remainder;
    }
}
