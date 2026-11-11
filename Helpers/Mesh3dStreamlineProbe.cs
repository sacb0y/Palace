namespace Palace.Helpers;

/// <summary>
/// Result of asking the Diligent D3D12 host for the native device + command list
/// NVIDIA Streamline would wrap later. Pure data — no Streamline SDK load here
/// (license / production DLLs stay out of this spike).
/// </summary>
public readonly record struct Mesh3dStreamlineProbe(
    bool DeviceOk,
    bool CommandListOk,
    nint Device,
    nint CommandList,
    string Detail)
{
    public static Mesh3dStreamlineProbe Unavailable(string detail) =>
        new(false, false, 0, 0, detail);

    public static Mesh3dStreamlineProbe FromPointers(nint device, nint commandList, string? detail = null)
    {
        var deviceOk = device != 0;
        var listOk = commandList != 0;
        var text = detail
            ?? (deviceOk && listOk
                ? "ID3D12Device + ID3D12GraphicsCommandList obtained"
                : deviceOk
                    ? "device ok; command list missing (begin a frame first)"
                    : "device pointer was null");
        return new Mesh3dStreamlineProbe(deviceOk, listOk, device, commandList, text);
    }

    public bool ReadyForStreamlineHooks => DeviceOk && CommandListOk;

    public string StatusLine =>
        ReadyForStreamlineHooks
            ? $"Streamline probe OK — {Detail}"
            : $"Streamline probe blocked — {Detail}";
}
