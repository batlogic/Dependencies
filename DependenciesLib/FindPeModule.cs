using System;
using System.IO;
using System.Collections.Generic;

using Dependencies.ClrPh;

namespace Dependencies
{
    public enum ModuleSearchStrategy
    {
        ROOT = -1,

        SxS = 0,
        ApiSetSchema = 1,
        WellKnownDlls = 2,
        ApplicationDirectory = 3,
        System32Folder = 4,
        WindowsFolder = 5,
        WorkingDirectory = 6,
        Environment = 7,
        AppInitDLL = 8,
        Fullpath = 9,
        ClrAssembly = 10,

        NOT_FOUND = 0xff
    };

    #region FindPe

    /// <summary>
    /// Dll path resolver emulator for the NT Loader.
    /// </summary>
    public class FindPe
    {
        static string FindPeFromPath(string ModuleName, List<string> CandidateFolders, bool Wow64Dll = false)
        {
            string PeFilePath = null;

            foreach (String CandidatePath in CandidateFolders)
            {
                PeFilePath = Path.Combine(CandidatePath, ModuleName);
                PE TestPe = BinaryCache.LoadPe(PeFilePath);

                if ((TestPe != null) && (TestPe.LoadSuccessful) && (TestPe.IsWow64Dll() == Wow64Dll))
                    return PeFilePath;
            }

            return null;
        }

        // default search order : 
        // https://msdn.microsoft.com/en-us/library/windows/desktop/ms682586(v=vs.85).aspx
        // 
        // if (SafeDllSearchMode) {
        //      -1. Sxs manifests
        //      0. KnownDlls list
        //      1. Loaded PE folder
        //      2. C:\Windows\(System32 | SysWow64 )
        //      3. 16-bit system directory   <-- ignored
        //      4. C:\Windows
        //      5. %pwd%
        //      6. AppDatas
        //      }
        public static Tuple<ModuleSearchStrategy, string> FindPeFromDefault(PE RootPe, string ModuleName, SxsEntries SxsCache)
        {
            bool Wow64Dll = RootPe.IsWow64Dll();
            string RootPeFolder = Path.GetDirectoryName(RootPe.Filepath);
            string FoundPePath = null;
            
            Environment.SpecialFolder WindowsSystemFolder = (Wow64Dll) ?
                Environment.SpecialFolder.SystemX86 :
                Environment.SpecialFolder.System;
            String WindowsSystemFolderPath = Environment.GetFolderPath(WindowsSystemFolder);


            // -1. Look in Sxs manifest (copious reversing needed)
            // TODO : find dll search order
            if (SxsCache.Count != 0)
            {
                SxsEntry Entry = SxsCache.Find( SxsItem =>
                    string.Equals(SxsItem.Name, ModuleName, StringComparison.OrdinalIgnoreCase)
                );

                if (Entry != null)
                {
                    return new Tuple<ModuleSearchStrategy, string>(ModuleSearchStrategy.SxS, Entry.Path);
                }
            }


            // 0. Look in well-known dlls list
            // HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Session Manager\KnownDLLs
            // https://blogs.msdn.microsoft.com/larryosterman/2004/07/19/what-are-known-dlls-anyway/
            String KnownDll = Phlib.GetKnownDlls(Wow64Dll).Find(x => string.Equals(x, ModuleName, StringComparison.OrdinalIgnoreCase));
            if (KnownDll != null)
            {
                return new Tuple<ModuleSearchStrategy, string>(
                    ModuleSearchStrategy.WellKnownDlls, 
                    Path.Combine(WindowsSystemFolderPath, KnownDll)
                );
            }


            // 1. Look in application folder
            FoundPePath = FindPeFromPath(ModuleName, new List<string>(new string[] { RootPeFolder }), Wow64Dll);
            if (FoundPePath != null)
            {
                return new Tuple<ModuleSearchStrategy, string>(
                    ModuleSearchStrategy.ApplicationDirectory,
                   FoundPePath
                );
            }

            // {2-3-4}. Look in system folders
            List<String> SystemFolders = new List<string>(new string[] {
                WindowsSystemFolderPath,
                Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                }
            );

            FoundPePath = FindPeFromPath(ModuleName, SystemFolders, Wow64Dll);
            if (FoundPePath != null)
            {
                return new Tuple<ModuleSearchStrategy, string>(
                    ModuleSearchStrategy.WindowsFolder,
                   FoundPePath
                );
            }

            // 5. Look in current directory
            // Ignored for the time being since we can't know from
            // where the exe is run
            // TODO : Add a user supplied path emulating %cwd%


            // 6. Look in local app data (check for python for exemple)



            // 7. Find in PATH
            string PATH = Environment.GetEnvironmentVariable("PATH");
            List<String> PATHFolders = new List<string>(PATH.Split(';'));
            FoundPePath = FindPeFromPath(ModuleName, PATHFolders, Wow64Dll);
            if (FoundPePath != null)
            {
                return new Tuple<ModuleSearchStrategy, string>(
                    ModuleSearchStrategy.Environment,
                   FoundPePath
                );
            }


            // 8. Check if it's an absolute import
            if (File.Exists(ModuleName))
            {
                return new Tuple<ModuleSearchStrategy, string>(
                   ModuleSearchStrategy.Fullpath,
                   ModuleName
               );
            }


            return new Tuple<ModuleSearchStrategy, string>(
                ModuleSearchStrategy.NOT_FOUND,
                null
            );
        }
    }
    #endregion FindPe
}
