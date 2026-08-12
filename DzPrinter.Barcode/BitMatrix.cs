namespace DzPrinter.Barcode;

/// <summary>
/// 位矩阵。对应 JS SDK 中 <c>u</c> 类。
/// 用于 QR/PDF417/DataMatrix 等二维条码的位图表示。
/// 内部以单字节数组存储每个像素（0/1），并维护独立的 reserved 位图记录"已被功能图形占用"的位置。
/// </summary>
public sealed class BitMatrix
{
    private byte[] _data;
    private byte[]? _reservedBit;

    /// <summary>列数（同时等于行数，QR 矩阵为方阵）。</summary>
    public int Cols { get; }

    /// <summary>行数。</summary>
    public int Rows { get; }

    /// <summary>原始位数据（每字节 0/1）。</summary>
    public byte[] Data => _data;

    public BitMatrix(int cols, int rows)
    {
        Cols = cols <= 0 ? 0 : cols;
        Rows = rows <= 0 ? 0 : rows;
        _data = new byte[Rows * Cols];
    }

    /// <summary>预留位数组（按需懒初始化）。对应 JS <c>u.reservedBit</c> getter。</summary>
    private byte[] ReservedBit
    {
        get
        {
            if (_reservedBit == null) _reservedBit = new byte[_data.Length];
            return _reservedBit;
        }
    }

    /// <summary>
    /// 设置 (row, col) 处的位值。对应 JS <c>u.set(t, e, i, s)</c>。
    /// </summary>
    public void Set(int row, int col, int value, bool reserve = false)
    {
        var idx = row * Cols + col;
        _data[idx] = (byte)(value != 0 ? 1 : 0);
        if (reserve) ReservedBit[idx] = 1;
    }

    /// <summary>设置 (row, col) 处的位值（布尔重载）。</summary>
    public void Set(int row, int col, bool value, bool reserve = false) =>
        Set(row, col, value ? 1 : 0, reserve);

    /// <summary>
    /// 获取 (row, col) 处的位值。对应 JS <c>u.get(t, e)</c>。
    /// </summary>
    public int Get(int row, int col) => _data[row * Cols + col];

    /// <summary>
    /// 异或 (row, col) 处的位值。对应 JS <c>u.xor(t, e, i)</c>。
    /// </summary>
    public void Xor(int row, int col, bool value) =>
        _data[row * Cols + col] ^= (byte)(value ? 1 : 0);

    /// <summary>
    /// 查询 (row, col) 是否已被功能图形占用。对应 JS <c>u.isReserved(t, e)</c>。
    /// </summary>
    public bool IsReserved(int row, int col) =>
        _reservedBit != null && _reservedBit[row * Cols + col] != 0;

    /// <summary>
    /// 取指定行的数据切片。对应 JS <c>u.getRowData(t)</c>。
    /// </summary>
    public byte[] GetRowData(int row)
    {
        var start = (row <= 0 ? 0 : row) * Cols;
        var result = new byte[Cols];
        Array.Copy(_data, start, result, 0, Cols);
        return result;
    }
}
