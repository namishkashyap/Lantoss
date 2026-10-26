// Compile separately: tests call the real COM implementation with isolated targets and no app launch.
#define LANTOSS_EXPLORER_TEST
#include "LantossExplorer.cpp"
#include <cstdio>

static int passed;
static void Check(bool condition, const char* label) {
    if (!condition) { std::fprintf(stderr, "FAIL %s\n", label); ExitProcess(1); }
    ++passed; std::printf("PASS %s\n", label);
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 3) return 2;
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    testExecutable = std::wstring(argv[1]) + L"\\Lantoss.exe";
    testRequest = std::wstring(argv[1]) + L"\\11111111-1111-1111-1111-111111111111.req";
    DeleteFileW(testRequest.c_str());
    std::wstring file = std::wstring(argv[1]) + L"\\Photo & family \U0001F600.txt";
    HANDLE created = CreateFileW(file.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    Check(created != INVALID_HANDLE_VALUE, "Unicode file fixture"); CloseHandle(created);
    Check(DllCanUnloadNow() == S_OK, "Unloaded before activation");
    IClassFactory* factory = nullptr;
    Check(DllGetClassObject(CommandId, IID_IClassFactory, reinterpret_cast<void**>(&factory)) == S_OK, "COM class factory export");
    Check(DllCanUnloadNow() == S_FALSE, "Live factory pins DLL");
    IExplorerCommand* root = nullptr;
    Check(factory->CreateInstance(nullptr, IID_IExplorerCommand, reinterpret_cast<void**>(&root)) == S_OK, "IExplorerCommand activation");
    PWSTR title = nullptr; root->GetTitle(nullptr, &title);
    Check(std::wstring(title) == L"Share with Lantoss", "Modern menu title"); CoTaskMemFree(title);
    EXPCMDFLAGS flags{}; root->GetFlags(&flags);
    Check(flags == ECF_HASSUBCOMMANDS, "Submenu advertised to Windows 11");
    EXPCMDSTATE state{}; root->GetState(nullptr, FALSE, &state);
    Check(state == ECS_HIDDEN, "No command without a file selection");
    IEnumExplorerCommand* enumeration = nullptr; root->EnumSubCommands(&enumeration);
    IExplorerCommand* picker = nullptr; ULONG fetched = 0;
    Check(enumeration->Next(1, &picker, &fetched) == S_OK && fetched == 1, "Choose a PC exists without saved devices");
    GUID rootName{}, pickerName{}; root->GetCanonicalName(&rootName); picker->GetCanonicalName(&pickerName);
    Check(rootName != pickerName, "Picker has a distinct stable shell command identity");
    IExplorerCommand* absent = nullptr;
    Check(enumeration->Next(1, &absent, &fetched) == S_FALSE && fetched == 0, "COM enumeration end");
    enumeration->Reset(); IEnumExplorerCommand* clone = nullptr; enumeration->Clone(&clone);
    Check(clone->Skip(1) == S_OK && clone->Next(1, &absent, &fetched) == S_FALSE, "Clone, reset and skip preserve cursor"); clone->Release(); enumeration->Release();
    IShellItem* item = nullptr; IShellItemArray* selection = nullptr;
    Check(SUCCEEDED(SHCreateItemFromParsingName(file.c_str(), nullptr, IID_IShellItem, reinterpret_cast<void**>(&item))) && SUCCEEDED(SHCreateShellItemArrayFromShellItem(item, IID_IShellItemArray, reinterpret_cast<void**>(&selection))), "Real Windows shell item selection");
    root->GetState(selection, FALSE, &state); Check(state == ECS_ENABLED, "Regular files enable the menu");
    Check(picker->Invoke(selection, nullptr) == S_OK, "Picker writes one complete selection request");
    picker->Release(); DeleteFileW(testRequest.c_str());
    selection->Release(); selection = nullptr;
    PIDLIST_ABSOLUTE ids[2]{};
    Check(SUCCEEDED(SHParseDisplayName(file.c_str(), nullptr, &ids[0], 0, nullptr)) && SUCCEEDED(SHParseDisplayName(argv[1], nullptr, &ids[1], 0, nullptr))
        && SUCCEEDED(SHCreateShellItemArrayFromIDLists(2, const_cast<PCIDLIST_ABSOLUTE*>(ids), &selection)), "Real mixed file and folder multiselection");
    CoTaskMemFree(ids[0]); CoTaskMemFree(ids[1]);
    testTargets = {{L"Family PC", std::wstring(64,L'A')}};
    root->EnumSubCommands(&enumeration); enumeration->Skip(1);
    IExplorerCommand* target = nullptr; enumeration->Next(1, &target, &fetched);
    target->GetTitle(nullptr, &title); Check(std::wstring(title) == L"Family PC", "Saved-PC submenu label"); CoTaskMemFree(title);
    GUID targetName{}, repeatedName{}; target->GetCanonicalName(&targetName); target->GetCanonicalName(&repeatedName);
    Check(targetName == repeatedName && targetName != pickerName && targetName != rootName, "Named PC has its own repeatable shell command identity");
    Check(target->Invoke(selection, nullptr) == S_OK, "Named PC selection writes fingerprint and Unicode path");
    target->Release(); enumeration->Release(); selection->Release(); item->Release(); root->Release(); factory->Release();
    Check(DllCanUnloadNow() == S_OK, "Every COM reference released");
    // Also activate the built shipping DLL through its actual exports (no registry changes).
    HMODULE shipping = LoadLibraryW(argv[2]); Check(shipping != nullptr, "Shipping native DLL loads without compiler runtime installation");
    using GetFactory = HRESULT (__stdcall*)(REFCLSID, REFIID, void**);
    using Unload = HRESULT (__stdcall*)();
    auto getFactory = reinterpret_cast<GetFactory>(reinterpret_cast<void*>(GetProcAddress(shipping, "DllGetClassObject")));
    auto canUnload = reinterpret_cast<Unload>(reinterpret_cast<void*>(GetProcAddress(shipping, "DllCanUnloadNow")));
    Check(getFactory && canUnload && canUnload() == S_OK && getFactory(CommandId, IID_IClassFactory, reinterpret_cast<void**>(&factory)) == S_OK, "Shipping COM exports activate factory");
    Check(factory->CreateInstance(nullptr, IID_IExplorerCommand, reinterpret_cast<void**>(&root)) == S_OK && canUnload() == S_FALSE, "Shipping IExplorerCommand activates with live lifetime tracking");
    root->GetTitle(nullptr, &title); Check(std::wstring(title) == L"Share with Lantoss", "Shipping command title"); CoTaskMemFree(title);
    root->Release(); factory->Release(); Check(canUnload() == S_OK, "Shipping DLL releases every COM object"); FreeLibrary(shipping);
    std::printf("%d native COM checks passed\n", passed);
    CoUninitialize(); return 0;
}
