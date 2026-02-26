namespace EndFieldFightHelper.Models;

public record GpuDeviceInfo(int DeviceId, string Name)
{
    public static readonly GpuDeviceInfo CpuDevice = new(-1, "CPU");

    public bool IsCpu => DeviceId < 0;

    public override string ToString() => IsCpu ? "CPU（不使用 GPU 加速）" : Name;
}
