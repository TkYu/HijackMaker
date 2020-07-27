using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace HijackMaker
{
    public class MainWindowVM : INotifyPropertyChanged
    {
        #region OnPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName]
            string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Properties

        private readonly string _systemFolder;
        private readonly List<string> _winDlls;

        private string[] _listedDlls;

        public string[] ListedDlls
        {
            get { return _listedDlls; }
            set
            {
                _listedDlls = value;
                OnPropertyChanged();
            }
        }

        private string _searchText;

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                _searchText = value;
                OnPropertyChanged();
                ListedDlls = string.IsNullOrEmpty(_searchText) ? _winDlls.ToArray() : _winDlls.Where(c => c.Contains(_searchText)).ToArray();
            }
        }

        private bool _withIndex = true;

        public bool WithIndex
        {
            get { return _withIndex; }
            set
            {
                _withIndex = value;
                OnPropertyChanged();
            }
        }

        private string _selectedDll;

        public string SelectedDll
        {
            get { return _selectedDll; }
            set
            {
                _selectedDll = value;
                OnPropertyChanged();
            }
        }


        private string[] _selectedDlls;

        public string[] SelectedDlls
        {
            get { return _selectedDlls; }
            set
            {
                _selectedDlls = value;
                OnPropertyChanged();
            }
        }

        private string _textResult;

        public string TextResult
        {
            get { return _textResult; }
            set
            {
                _textResult = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region Command

        private ICommand _cmdAdd;

        public ICommand CmdAdd => _cmdAdd ?? (_cmdAdd = new SimpleCommand
        {
            CanExecuteDelegate = x => x is string y && !string.IsNullOrEmpty(y),
            ExecuteDelegate = x =>
            {
                if (x is string y)
                {
                    if (SelectedDlls?.Contains(y) ?? false) return;
                    if (SelectedDlls?.Any() ?? false) return;
                    SelectedDlls = SelectedDlls?.Concat(new[] {y}).ToArray() ?? new[] {y};
                    MakeHijack();
                }
            }
        });

        private ICommand _cmdRemove;

        public ICommand CmdRemove => _cmdRemove ?? (_cmdRemove = new SimpleCommand
        {
            CanExecuteDelegate = x => x is string y && !string.IsNullOrEmpty(y),
            ExecuteDelegate = x =>
            {
                if (x is string y)
                {
                    if (SelectedDlls == null) return;
                    SelectedDlls = SelectedDlls.Where(c => c != y).ToArray();
                    MakeHijack();
                }
            }
        });


        private void MakeHijack()
        {
            if (SelectedDlls == null || SelectedDlls.Length == 0)
            {
                TextResult = "";
                return;
            }

            //var exports = new Symbol(SelectedDlls).Exports;
            var exports = SelectedDlls.Select(selectedDll => new PeNet.PeFile(selectedDll)).SelectMany(pefile => pefile.ExportedFunctions)
                .ToDictionary(k => k.Name ?? "NONAME" + k.Ordinal.ToString("D3"), v => v.Ordinal);
            var fileNames = SelectedDlls.Select(Path.GetFileNameWithoutExtension).ToArray();

            TextResult = $@"#include <windows.h>
#include <intrin.h>

namespace hijack
{{


#define NOP_FUNC {{ \
    __nop();\
    __nop();\
    __nop();\
    __nop();\
    __nop();\
    __nop();\
    __nop();\
    __nop();\
    __nop();\
    __nop();\
    __nop();\
    __nop();\
    return __COUNTER__;\
}}
// 用 __COUNTER__ 来生成一点不一样的代码，避免被 VS 自动合并相同函数

{(WithIndex ? "" : @"#define PRAGMA(api) comment(linker, ""/EXPORT:"" #api ""="" PREFIX #api)")}
#define EXPORT(api) int __cdecl api() NOP_FUNC


#pragma region 声明导出函数
// 声明导出函数
{string.Join("\n", exports.Select(c => WithIndex ?
(c.Key.StartsWith("NONAME") ? $"#pragma comment(linker, \"/export:{c.Key}=?{c.Key}@hijack@@YAHXZ,@{c.Value},NONAME\")" : $"#pragma comment(linker, \"/export:{c.Key}=?{c.Key}@hijack@@YAHXZ,@{c.Value}\")") :
$"#pragma PRAGMA({c.Key})"))}

{string.Join("\n", exports.Select(c => $"EXPORT({c.Key})"))}
}}
#pragma endregion

#pragma region 还原导出函数
bool WriteMemory(PBYTE BaseAddress, PBYTE Buffer, DWORD nSize)
{{
    DWORD ProtectFlag = 0;
    if (VirtualProtectEx(GetCurrentProcess(), BaseAddress, nSize, PAGE_EXECUTE_READWRITE, &ProtectFlag))
    {{
        memcpy(BaseAddress, Buffer, nSize);
        FlushInstructionCache(GetCurrentProcess(), BaseAddress, nSize);
        VirtualProtectEx(GetCurrentProcess(), BaseAddress, nSize, ProtectFlag, &ProtectFlag);
        return true;
    }}
    return false;
}}

// 定义MWORD为机器字长
#include <stdint.h>
#ifdef _WIN64
typedef uint64_t MWORD;
#else
typedef uint32_t MWORD;
#endif

// 还原导出函数
void InstallJMP(PBYTE BaseAddress, MWORD Function)
{{
#ifdef _WIN64
    BYTE move[] = {{0x48, 0xB8}};//move rax,xxL);
    BYTE jump[] = {{0xFF, 0xE0}};//jmp rax

    WriteMemory(BaseAddress, move, sizeof(move));
    BaseAddress += sizeof(move);

    WriteMemory(BaseAddress, (PBYTE)&Function, sizeof(MWORD));
    BaseAddress += sizeof(MWORD);

    WriteMemory(BaseAddress, jump, sizeof(jump));
#else
    BYTE jump[] = {{0xE9}};
    WriteMemory(BaseAddress, jump, sizeof(jump));
    BaseAddress += sizeof(jump);

    MWORD offset = Function - (MWORD)BaseAddress - 4;
    WriteMemory(BaseAddress, (PBYTE)&offset, sizeof(offset));
#endif // _WIN64
}}
#pragma endregion

#pragma region 加载系统dll
{string.Join("\n\n",fileNames.Select(c=>$@"void Load{CultureInfo.CurrentCulture.TextInfo.ToTitleCase(c)}(HINSTANCE hModule)
{{
    PBYTE pImageBase = (PBYTE)hModule;
    PIMAGE_DOS_HEADER pimDH = (PIMAGE_DOS_HEADER)pImageBase;
    if (pimDH->e_magic == IMAGE_DOS_SIGNATURE)
    {{
        PIMAGE_NT_HEADERS pimNH = (PIMAGE_NT_HEADERS)(pImageBase + pimDH->e_lfanew);
        if (pimNH->Signature == IMAGE_NT_SIGNATURE)
        {{
            PIMAGE_EXPORT_DIRECTORY pimExD = (PIMAGE_EXPORT_DIRECTORY)(pImageBase + pimNH->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT].VirtualAddress);
            DWORD*  pName = (DWORD*)(pImageBase + pimExD->AddressOfNames);
            DWORD*  pFunction = (DWORD*)(pImageBase + pimExD->AddressOfFunctions);
            WORD*  pNameOrdinals = (WORD*)(pImageBase + pimExD->AddressOfNameOrdinals);

            wchar_t szSysDirectory[MAX_PATH + 1];
            GetSystemDirectory(szSysDirectory, MAX_PATH);

            wchar_t szDLLPath[MAX_PATH + 1];
            lstrcpy(szDLLPath, szSysDirectory);
            lstrcat(szDLLPath, TEXT(""\\{c}.dll""));

            HINSTANCE module = LoadLibrary(szDLLPath);
            for (size_t i = 0; i < pimExD->NumberOfNames; i++)
            {{
                MWORD Original = (MWORD)GetProcAddress(module, (char*)(pImageBase + pName[i]));
                if (Original)
                {{
                    InstallJMP(pImageBase + pFunction[pNameOrdinals[i]], Original);
                }}
            }}
        }}
    }}
}}"))}
#pragma endregion

void LoadSysDll(HINSTANCE hModule)
{{
{string.Join("\n", fileNames.Select(c => $"	Load{CultureInfo.CurrentCulture.TextInfo.ToTitleCase(c)}(hModule);"))}
}}
";
        }

        #endregion

        public MainWindowVM()
        {
            _systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.System);
            _winDlls = Directory.GetFiles(_systemFolder, "*.dll", SearchOption.TopDirectoryOnly).ToList();
            if (Environment.Is64BitOperatingSystem)
            {
                _winDlls.AddRange(Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "*.dll", SearchOption.TopDirectoryOnly));
            }
            if (Environment.GetCommandLineArgs().Length > 1 && Environment.GetCommandLineArgs().Any(c => c.ToLower().EndsWith(".dll")))
            {
                SelectedDlls = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToArray();
                MakeHijack();
            }
            else
            {
                SearchText = "ws2_32";
            }
        }
    }


    public class SimpleCommand : ICommand
    {
        public Predicate<object> CanExecuteDelegate { get; set; }
        public Action<object> ExecuteDelegate { get; set; }

        public bool CanExecute(object parameter)
        {
            return CanExecuteDelegate == null || CanExecuteDelegate(parameter);
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public void Execute(object parameter)
        {
            ExecuteDelegate?.Invoke(parameter);
        }
    }
}