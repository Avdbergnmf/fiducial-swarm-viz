// OS folder dialog. Editor uses Unity's panel; Windows players use IFileDialog.

using System;
using System.IO;
using UnityEngine;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace SwarmViewer
{
    public static class NativeFolderPicker
    {
        public static string Open(string title, string startingDirectory)
        {
#if UNITY_EDITOR
            string start = Directory.Exists(startingDirectory) ? startingDirectory : "";
            return UnityEditor.EditorUtility.OpenFolderPanel(title, start, "");
#elif UNITY_STANDALONE_WIN
            return WindowsFolderDialog.Open(title, startingDirectory);
#else
            Debug.LogWarning("[viewer] Folder picker is only available in the Editor and Windows builds. Paste an absolute path instead.");
            return "";
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        static class WindowsFolderDialog
        {
            const uint FOS_PICKFOLDERS = 0x00000020;
            const uint FOS_FORCEFILESYSTEM = 0x00000040;
            const uint SIGDN_FILESYSPATH = 0x80058000;

            public static string Open(string title, string startingDirectory)
            {
                var dialog = (IFileDialog)new FileOpenDialogRCW();
                try
                {
                    dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);
                    if (!string.IsNullOrEmpty(title))
                        dialog.SetTitle(title);

                    if (!string.IsNullOrEmpty(startingDirectory) && Directory.Exists(startingDirectory))
                    {
                        int hr = SHCreateItemFromParsingName(
                            startingDirectory, IntPtr.Zero, typeof(IShellItem).GUID, out IShellItem folder);
                        if (hr == 0 && folder != null)
                            dialog.SetFolder(folder);
                    }

                    if (dialog.Show(GetActiveWindow()) != 0)
                        return "";

                    dialog.GetResult(out IShellItem item);
                    if (item == null) return "";

                    item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr pszPath);
                    if (pszPath == IntPtr.Zero) return "";
                    try
                    {
                        return Marshal.PtrToStringUni(pszPath) ?? "";
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pszPath);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[viewer] Folder picker failed: {e.Message}");
                    return "";
                }
                finally
                {
                    Marshal.ReleaseComObject(dialog);
                }
            }

            [DllImport("user32.dll")]
            static extern IntPtr GetActiveWindow();

            [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
            static extern int SHCreateItemFromParsingName(
                [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
                IntPtr pbc,
                [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
                out IShellItem ppv);

            [ComImport]
            [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
            class FileOpenDialogRCW { }

            [ComImport]
            [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IFileDialog
            {
                [PreserveSig] int Show(IntPtr parent);
                void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
                void SetFileTypeIndex(uint iFileType);
                void GetFileTypeIndex(out uint piFileType);
                void Advise(IntPtr pfde, out uint pdwCookie);
                void Unadvise(uint dwCookie);
                void SetOptions(uint fos);
                void GetOptions(out uint pfos);
                void SetDefaultFolder(IShellItem psi);
                void SetFolder(IShellItem psi);
                void GetFolder(out IShellItem ppsi);
                void GetCurrentSelection(out IShellItem ppsi);
                void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
                void GetFileName(out IntPtr pszName);
                void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
                void GetTitle(out IntPtr pszTitle);
                void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
                void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
                void GetResult(out IShellItem ppsi);
                void AddPlace(IShellItem psi, int fdap);
                void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
                void Close(int hr);
                void SetClientGuid(ref Guid guid);
                void ClearClientData();
                void SetFilter(IntPtr pFilter);
            }

            [ComImport]
            [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IShellItem
            {
                void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
                void GetParent(out IShellItem ppsi);
                void GetDisplayName(uint sigdnName, out IntPtr ppszName);
                void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
                void Compare(IShellItem psi, uint hint, out int piOrder);
            }
        }
#endif
    }
}
