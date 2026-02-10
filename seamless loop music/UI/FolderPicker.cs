using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace seamless_loop_music
{
    /// <summary>
    /// 基于 Windows COM 接口实现的现代文件夹选择器
    /// 支持地址栏输入、粘贴路径
    /// </summary>
    public class FolderPicker
    {
        public string ResultPath { get; private set; }

        public bool ShowDialog(Window owner)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();
            try
            {
                // FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM
                // 设置为“只选文件夹”且“强制文件系统路径”
                dialog.SetOptions(0x20 | 0x40); 
                
                var ptr = new WindowInteropHelper(owner).Handle;
                
                // 显示对话框
                int allowedMethods = dialog.Show(ptr); // S_OK = 0
                
                if (allowedMethods == 0) 
                {
                    IShellItem item;
                    dialog.GetResult(out item);
                    string path;
                    // SIGDN_FILESYSPATH = 0x80058000
                    item.GetDisplayName(0x80058000, out path);
                    ResultPath = path;
                    return true;
                }
            }
            catch (COMException)
            {
                // 用户取消时可能会抛出异常，忽略即可
            }
            catch (Exception)
            {
                // 其他错误
            }
            return false;
        }

        // --- COM 接口定义 (就像在 C++ 里写 IUnknown 一样) ---

        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        [ClassInterface(ClassInterfaceType.None)]
        private class FileOpenDialog { }

        [ComImport]
        [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            // 为了节省代码量，这里只定义我们需要用到的虚函数表槽位 (VTable)
            // 注意：顺序必须严格正确！
            
            // IModalWindow methods
            [PreserveSig] int Show(IntPtr parent);

            // IFileDialog methods
            void SetFileTypes();     // 占位
            void SetFileTypeIndex(); // 占位
            void GetFileTypeIndex(); // 占位
            void Advise();           // 占位
            void Unadvise();         // 占位
            void SetOptions(uint fos); // <--- 我们只要这个
            void GetOptions();       // 占位
            void SetDefaultFolder(); // 占位
            void SetFolder();        // 占位
            void GetFolder();        // 占位
            void GetCurrentSelection(); // 占位
            void SetFileName();      // 占位
            void GetFileName();      // 占位
            void SetTitle();         // 占位
            void SetOkButtonLabel(); // 占位
            void SetFileNameLabel(); // 占位
            void GetResult(out IShellItem pItem); // <--- 和这个
            void AddPlace();         // 占位
            void SetDefaultExtension(); // 占位
            void Close();            // 占位
            void SetClientGuid();    // 占位
            void ClearClientData();  // 占位
            void SetFilter();        // 占位
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(); // 占位
            void GetParent();     // 占位
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName); // <--- 只要这个
            void GetAttributes(); // 占位
            void Compare();       // 占位
        }
    }
}
