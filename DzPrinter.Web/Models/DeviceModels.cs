namespace DzPrinter.Web.Models;

// =====================================================================
// 设备管理 DTO
// =====================================================================

public sealed class DiscoverResponse
{
    public List<DeviceDto> Devices { get; set; } = new();
}

public sealed class DeviceDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public int Rssi { get; set; }
    public string DeviceType { get; set; } = string.Empty;
}

public sealed class ConnectRequest
{
    public string DeviceId { get; set; } = string.Empty;
}

public sealed class ConnectResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DeviceDto? Device { get; set; }
}

public sealed class StatusResponse
{
    public bool IsConnected { get; set; }
    public DeviceDto? Device { get; set; }
    public string? State { get; set; }
}

public sealed class PrinterInfoResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public HardwareInfoDto? Hardware { get; set; }
}

public sealed class HardwareInfoDto
{
    public int Dpi { get; set; }
    public int PrinterWidth { get; set; }
    public int BufferSize { get; set; }
    public int BatteryCount { get; set; }
    public double BatteryVoltage { get; set; }
    public bool ChargeStatus { get; set; }
}

public sealed class PrintableStatusResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? StatusCode { get; set; }
}

public sealed class PrintResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int ChunksSent { get; set; }
    public string? PreviewPath { get; set; }
    public string? PreviewBase64 { get; set; }
}

public sealed class RawPrintRequest
{
    /// <summary>Base64 编码的原始打印数据。</summary>
    public string Base64Data { get; set; } = string.Empty;
}

public sealed class ApiResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
