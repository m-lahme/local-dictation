using System.Runtime.InteropServices;

// Enumerate Vulkan before whisper.cpp initializes. Pass the selected physical-device index explicitly to WhisperFactoryOptions.GpuDevice.
internal static class VulkanDevice
{
    [StructLayout(LayoutKind.Sequential)] struct InstanceInfo { public uint type; public IntPtr next; public uint flags; public IntPtr application; public uint layerCount; public IntPtr layers; public uint extensionCount; public IntPtr extensions; }
    [DllImport("vulkan-1.dll")] static extern int vkCreateInstance(ref InstanceInfo info, IntPtr allocator, out IntPtr instance);
    [DllImport("vulkan-1.dll")] static extern int vkEnumeratePhysicalDevices(IntPtr instance, ref uint count, [Out] IntPtr[]? devices);
    [DllImport("vulkan-1.dll")] static extern void vkGetPhysicalDeviceProperties(IntPtr device, IntPtr properties);
    [DllImport("vulkan-1.dll")] static extern void vkDestroyInstance(IntPtr instance, IntPtr allocator);
    public static int Index { get; private set; }
    public static string SelectDiscrete() { var info = new InstanceInfo { type = 1 }; if (vkCreateInstance(ref info, IntPtr.Zero, out var instance) != 0) throw new InvalidOperationException(Messages.T("Vulkan could not be initialized.", "Vulkan konnte nicht initialisiert werden.")); try { uint count = 0; vkEnumeratePhysicalDevices(instance, ref count, null); var devices = new IntPtr[count]; vkEnumeratePhysicalDevices(instance, ref count, devices); string name = ""; int selected = -1; var p = Marshal.AllocHGlobal(4096); try { for (int i = 0; i < count; i++) { vkGetPhysicalDeviceProperties(devices[i], p); int vendor = Marshal.ReadInt32(p, 8), type = Marshal.ReadInt32(p, 16); if (type == 2 && (selected < 0 || vendor == 0x10de)) { selected = i; name = Marshal.PtrToStringUTF8(p + 20) ?? "Vulkan GPU"; if (vendor == 0x10de) break; } } } finally { Marshal.FreeHGlobal(p); } if (selected < 0) throw new InvalidOperationException(Messages.T("No dedicated Vulkan GPU available.", "Keine dedizierte Vulkan-GPU verfügbar.")); Index = selected; return name; } finally { vkDestroyInstance(instance, IntPtr.Zero); } }
}
