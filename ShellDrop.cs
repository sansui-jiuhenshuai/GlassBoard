using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace GlassBoard;

internal sealed record DockItem(string Target, Guid? ClassId, string Name, BitmapSource Icon);

internal static class ShellDrop
{
    private const string ShellIdList = "Shell IDList Array";
    private static readonly Guid Computer = new("20D04FE0-3AEA-1069-A2D8-08002B30309D");
    private static readonly Guid RecycleBin = new("645FF040-5081-101B-9F08-00AA002F954E");

    internal static DockItem? Resolve(string target)
    {
        if (!IsAppPath(target) && !(target.StartsWith("::", StringComparison.Ordinal) &&
            Guid.TryParse(target.AsSpan(2), out var id) && (id == Computer || id == RecycleBin)))
            return null;
        if (SHParseDisplayName(target, 0, out nint pidl, 0, out _) < 0)
            return null;
        var result = new List<DockItem>(1);
        AddOwnedPidl(pidl, new(StringComparer.OrdinalIgnoreCase), result);
        return result.Count == 0 ? null : result[0];
    }

    internal static bool Supports(IDataObject data) =>
        data.GetDataPresent(ShellIdList, false) || data.GetDataPresent(DataFormats.FileDrop, false);

    internal static List<DockItem> Read(IDataObject data)
    {
        var result = new List<DockItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var iid = typeof(IShellItemArray).GUID;
        // Let the Shell interpret CIDA parent/child PIDLs, including desktop virtual items.
        if (data is ComDataObject native &&
            SHCreateShellItemArrayFromDataObject(native, ref iid, out var items) >= 0)
        {
            try
            {
                items.GetCount(out uint count);
                for (uint index = 0; index < count; index++)
                {
                    if (items.GetItemAt(index, out object item) < 0)
                        continue;
                    try
                    {
                        if (SHGetIDListFromObject(item, out nint pidl) >= 0)
                            AddOwnedPidl(pidl, seen, result);
                    }
                    finally { Marshal.ReleaseComObject(item); }
                }
            }
            finally { Marshal.ReleaseComObject(items); }
        }

        if (data.GetData(DataFormats.FileDrop, false) is string[] paths)
            foreach (string path in paths)
                if (IsAppPath(path) && !seen.Contains(path) &&
                    SHParseDisplayName(path, 0, out nint pidl, 0, out _) >= 0)
                    AddOwnedPidl(pidl, seen, result);
        return result;
    }

    private static bool IsAppPath(string path) => Path.IsPathFullyQualified(path) &&
        (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) && File.Exists(path);

    private static void AddOwnedPidl(nint pidl, HashSet<string> seen, List<DockItem> result)
    {
        const uint FileSystemPath = 0x80058000, DesktopAbsoluteParsing = 0x80028000;
        const uint PidlIconAndDisplayName = 0x008 | 0x100 | 0x200;
        try
        {
            Guid? classId = null;
            string? target = NameFromPidl(pidl, FileSystemPath);
            if (target is null)
            {
                string? name = NameFromPidl(pidl, DesktopAbsoluteParsing);
                if (name is null || !name.StartsWith("::", StringComparison.Ordinal) ||
                    !Guid.TryParseExact(name.AsSpan(2), "B", out Guid id) ||
                    (id != Computer && id != RecycleBin))
                    return;
                classId = id;
                target = $"::{id:B}";
            }
            else if (!IsAppPath(target))
                return;

            if (seen.Contains(target) || SHGetFileInfoW(pidl, 0, out var info,
                (uint)Marshal.SizeOf<FileInfoNative>(), PidlIconAndDisplayName) == 0)
                return;
            try
            {
                if (info.Icon == 0)
                    return;
                var icon = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty,
                                                               BitmapSizeOptions.FromEmptyOptions());
                icon.Freeze();
                result.Add(new DockItem(target, classId, info.Name, icon));
                seen.Add(target);
            }
            finally { if (info.Icon != 0) DestroyIcon(info.Icon); }
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private static string? NameFromPidl(nint pidl, uint kind)
    {
        if (SHGetNameFromIDList(pidl, kind, out nint name) < 0)
            return null;
        try { return Marshal.PtrToStringUni(name); }
        finally { Marshal.FreeCoTaskMem(name); }
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        // Unused vtable slots precede GetCount and GetItemAt.
        void BindToHandler();
        void GetPropertyStore();
        void GetPropertyDescriptionList();
        void GetAttributes();
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemAt(uint index, [MarshalAs(UnmanagedType.IUnknown)] out object item);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfoNative
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Type;
    }

    [DllImport("shell32.dll")]
    private static extern int SHCreateShellItemArrayFromDataObject(ComDataObject data, ref Guid iid,
                                                                   out IShellItemArray items);
    [DllImport("shell32.dll")]
    private static extern int SHGetIDListFromObject([MarshalAs(UnmanagedType.IUnknown)] object item, out nint pidl);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(string name, nint context, out nint pidl, uint mask, out uint attributes);
    [DllImport("shell32.dll")]
    private static extern int SHGetNameFromIDList(nint pidl, uint kind, out nint name);
    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern nuint SHGetFileInfoW(nint pidl, uint attributes, out FileInfoNative info, uint size, uint flags);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);
}
