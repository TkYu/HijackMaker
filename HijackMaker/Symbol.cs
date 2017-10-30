using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace HijackMaker
{
    public class Symbol
    {
        #region DllImport
        //C:\Program Files (x86)\Windows Kits\10\Debuggers

        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymInitialize(IntPtr hProcess, string UserSearchPath, [MarshalAs(UnmanagedType.Bool)]bool fInvadeProcess);

        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymCleanup(IntPtr hProcess);

        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern ulong SymLoadModuleEx(IntPtr hProcess, IntPtr hFile, string ImageName, string ModuleName, long BaseOfDll, int DllSize, IntPtr Data, int Flags);

        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymEnumerateSymbols64(IntPtr hProcess, ulong BaseOfDll, SymEnumerateSymbolsProc64 EnumSymbolsCallback, IntPtr UserContext);

        public delegate bool SymEnumerateSymbolsProc64(string SymbolName, ulong SymbolAddress, uint SymbolSize, IntPtr UserContext);

        #endregion

        public string[] Exports => exports.ToArray();

        private List<string> exports = new List<string>();
        public Symbol(params string[] dllNames)
        {
            foreach (var dllName in dllNames)
            {
                IntPtr hCurrentProcess = System.Diagnostics.Process.GetCurrentProcess().Handle;

                ulong baseOfDll;
                bool status;

                // Initialize sym.
                // Please read the remarks on MSDN for the hProcess
                // parameter.
                status = SymInitialize(hCurrentProcess, null, false);

                if (status == false)
                {
                    Console.Out.WriteLine("Failed to initialize sym.");
                    return;
                }

                // Load dll.
                baseOfDll = SymLoadModuleEx(hCurrentProcess,
                    IntPtr.Zero,
                    dllName,
                    null,
                    0,
                    0,
                    IntPtr.Zero,
                    0);

                if (baseOfDll == 0)
                {
                    Console.Out.WriteLine("Failed to load module.");
                    SymCleanup(hCurrentProcess);
                    return;
                }

                // Enumerate symbols. For every symbol the 
                // callback method EnumSyms is called.
                if (SymEnumerateSymbols64(hCurrentProcess,
                        baseOfDll, EnumSyms, IntPtr.Zero) == false)
                {
                    Console.Out.WriteLine("Failed to enum symbols.");
                }

                // Cleanup.
                SymCleanup(hCurrentProcess);
            }
        }

        private bool EnumSyms(string name, ulong address, uint size, IntPtr context)
        {
            exports.Add(name);
            //Console.Out.WriteLine($"{name}/{address}/{size}");
            return true;
        }


    }
}
