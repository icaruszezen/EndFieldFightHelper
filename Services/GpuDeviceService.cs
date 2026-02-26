using System;
using System.Collections.Generic;
using EndFieldFightHelper.Models;
using Vortice.DXGI;

namespace EndFieldFightHelper.Services;

public static class GpuDeviceService
{
    public static List<GpuDeviceInfo> EnumerateAllDevices()
    {
        var devices = new List<GpuDeviceInfo> { GpuDeviceInfo.CpuDevice };
        try
        {
            var seen = new HashSet<(uint vendorId, uint deviceId)>();
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                using (adapter)
                {
                    var desc = adapter.Description1;
                    if ((desc.Flags & AdapterFlags.Software) != 0)
                        continue;
                    if (!seen.Add((desc.VendorId, desc.DeviceId)))
                        continue;
                    devices.Add(new GpuDeviceInfo((int)i, desc.Description.TrimEnd('\0')));
                }
            }
        }
        catch
        {
            // DXGI unavailable
        }
        return devices;
    }
}
