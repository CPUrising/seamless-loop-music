using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace seamless_loop_music.UI
{
    /// <summary>
    /// 文件夹选择结果：区分"取消"、"解析失败"与"可用目录"。
    /// </summary>
    public sealed class FolderPickerResult
    {
        public bool Canceled { get; set; }

        /// <summary>已解析为真实文件系统目录的路径（库已展开为成员目录）。</summary>
        public List<string> Folders { get; } = new List<string>();

        /// <summary>用户选中但无法解析为文件系统目录的项（如 此电脑、网络、解析失败的库）。</summary>
        public List<string> Unresolved { get; } = new List<string>();

        /// <summary>非取消的真实错误描述；为空表示没有错误。</summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 基于 Windows COM 接口实现的现代文件夹选择器。
    /// 支持地址栏输入、粘贴路径、多选，以及把「库」展开为库内真实文件夹。
    /// </summary>
    public class FolderPicker
    {
        private const int S_OK = 0;
        private const int HRESULT_CANCELLED = unchecked((int)0x800704C7);

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_ALLOWMULTISELECT = 0x00000200;

        private const uint SIGDN_NORMALDISPLAY = 0x00000000;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        private const uint LFF_FORCEFILESYSTEM = 0x00000001;
        private const uint STGM_READ = 0x00000000;

        public FolderPickerResult PickFolders(Window owner)
        {
            var result = new FolderPickerResult();
            IFileOpenDialog dialog = null;
            IShellItemArray items = null;

            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialog();
                // FOS_PICKFOLDERS：只选文件夹
                // FOS_ALLOWMULTISELECT：支持 Ctrl/Shift 多选
                // 注意：不能再加 FOS_FORCEFILESYSTEM，否则「库」等虚拟项无法选中
                dialog.SetOptions(FOS_PICKFOLDERS | FOS_ALLOWMULTISELECT);

                int hr = dialog.Show(GetOwnerHandle(owner));
                if (hr == HRESULT_CANCELLED)
                {
                    result.Canceled = true;
                    return result;
                }
                if (hr != S_OK)
                {
                    result.ErrorMessage = $"IFileOpenDialog.Show failed: 0x{hr:X8}";
                    return result;
                }

                dialog.GetResults(out items);
                CollectItems(items, result);
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
            finally
            {
                if (items != null) Marshal.ReleaseComObject(items);
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
        }

        private static IntPtr GetOwnerHandle(Window owner)
        {
            if (owner == null) return IntPtr.Zero;
            return new WindowInteropHelper(owner).Handle;
        }

        private static void CollectItems(IShellItemArray items, FolderPickerResult result)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            items.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                items.GetItemAt(i, out IShellItem item);
                try
                {
                    string path = GetFileSystemPath(item);
                    if (!string.IsNullOrEmpty(path))
                    {
                        if (seen.Add(path)) result.Folders.Add(path);
                        continue;
                    }

                    var libraryFolders = ResolveLibraryFolders(item);
                    if (libraryFolders.Count > 0)
                    {
                        foreach (var folder in libraryFolders)
                        {
                            if (seen.Add(folder)) result.Folders.Add(folder);
                        }
                        continue;
                    }

                    string displayName = GetDisplayName(item, SIGDN_NORMALDISPLAY);
                    result.Unresolved.Add(string.IsNullOrEmpty(displayName) ? "(unknown)" : displayName);
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }

        private static string GetFileSystemPath(IShellItem item)
        {
            string path = GetDisplayName(item, SIGDN_FILESYSPATH);
            return !string.IsNullOrEmpty(path) && Directory.Exists(path) ? path : null;
        }

        private static string GetDisplayName(IShellItem item, uint sigdn)
        {
            IntPtr pointer;
            if (item.GetDisplayName(sigdn, out pointer) != S_OK || pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }

        /// <summary>把选中的「库」展开为库内真实文件夹；不是库或解析失败返回空列表。</summary>
        private static List<string> ResolveLibraryFolders(IShellItem item)
        {
            var folders = new List<string>();
            IShellLibrary library = null;

            try
            {
                library = (IShellLibrary)new ShellLibrary();
                if (library.LoadLibraryFromItem(item, STGM_READ) != S_OK)
                {
                    return folders;
                }

                library.GetFolders(LFF_FORCEFILESYSTEM, typeof(IShellItemArray).GUID, out object arrayObject);
                var libraryItems = arrayObject as IShellItemArray;
                if (libraryItems == null)
                {
                    return folders;
                }

                try
                {
                    libraryItems.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        libraryItems.GetItemAt(i, out IShellItem child);
                        try
                        {
                            string path = GetFileSystemPath(child);
                            if (!string.IsNullOrEmpty(path)) folders.Add(path);
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(child);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(libraryItems);
                }
            }
            catch
            {
                // 非库项或解析失败：交由调用方按 Unresolved 处理
                folders.Clear();
            }
            finally
            {
                if (library != null) Marshal.ReleaseComObject(library);
            }

            return folders;
        }

        // --- COM 接口定义 ---
        // VTable 槽位顺序严格按 Windows SDK ShObjIdl_core.h（10.0.26100.0）核对

        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        [ClassInterface(ClassInterfaceType.None)]
        private class FileOpenDialog { }

        [ComImport]
        [Guid("D9B3211D-E57F-4426-AAEF-30A806ADD397")]
        [ClassInterface(ClassInterfaceType.None)]
        private class ShellLibrary { }

        [ComImport]
        [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            // IModalWindow
            [PreserveSig] int Show(IntPtr parent);

            // IFileDialog
            void SetFileTypes();
            void SetFileTypeIndex();
            void GetFileTypeIndex();
            void Advise();
            void Unadvise();
            void SetOptions(uint fos);
            void GetOptions();
            void SetDefaultFolder();
            void SetFolder();
            void GetFolder();
            void GetCurrentSelection();
            void SetFileName();
            void GetFileName();
            void SetTitle();
            void SetOkButtonLabel();
            void SetFileNameLabel();
            void GetResult(out IShellItem pItem);
            void AddPlace();
            void SetDefaultExtension();
            void Close();
            void SetClientGuid();
            void ClearClientData();
            void SetFilter();

            // IFileOpenDialog
            void GetResults(out IShellItemArray ppenum);
            void GetSelectedItems();
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler();
            void GetParent();
            [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes();
            void Compare();
        }

        [ComImport]
        [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler();
            void GetPropertyStore();
            void GetPropertyDescriptionList();
            void GetAttributes();
            void GetCount(out uint pdwNumItems);
            void GetItemAt(uint dwIndex, out IShellItem ppsi);
            void EnumItems();
        }

        [ComImport]
        [Guid("11a66efa-382e-451a-9234-1e0e12ef3085")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLibrary
        {
            [PreserveSig] int LoadLibraryFromItem(IShellItem psiLibrary, uint grfMode);
            void LoadLibraryFromKnownFolder();
            void AddFolder();
            void RemoveFolder();
            void GetFolders(uint lff, Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            void ResolveFolder();
            void GetDefaultSaveFolder();
            void SetDefaultSaveFolder();
            void GetOptions();
            void SetOptions();
            void GetFolderType();
            void SetFolderType();
            void GetIcon();
            void SetIcon();
            void Commit();
            void Save();
            void SaveInKnownFolder();
        }
    }
}
