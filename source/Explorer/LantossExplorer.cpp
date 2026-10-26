// Native COM only: Explorer's menu path never loads the CLR or starts discovery.
#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#undef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00
#include <windows.h>
#include <shobjidl.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <string>
#include <vector>
#include <algorithm>
#include <new>

static const CLSID CommandId = {0x18f23026,0x095d,0x4fd5,{0xae,0xf4,0x7d,0xdf,0x06,0x84,0x7d,0xe9}};
static HMODULE module;
static LONG objects, locks;
struct Target { std::wstring name, key; };
static constexpr wchar_t Settings[] = L"Software\\Lantoss\\Desktop";
#ifdef LANTOSS_EXPLORER_TEST
static std::wstring testRequest, testExecutable;
static std::vector<Target> testTargets;
#endif

static bool HexKey(const std::wstring& key) {
    return key.size() == 64 && std::all_of(key.begin(), key.end(), [](wchar_t c) {
        return (c >= L'0' && c <= L'9') || (c >= L'A' && c <= L'F') || (c >= L'a' && c <= L'f');
    });
}
static bool Enabled() {
#ifdef LANTOSS_EXPLORER_TEST
    return true;
#else
    DWORD value = 0, size = sizeof(value);
    return RegGetValueW(HKEY_CURRENT_USER, Settings, L"ExplorerEnabled", RRF_RT_REG_DWORD, nullptr, &value, &size) == ERROR_SUCCESS && value == 1;
#endif
}
static std::vector<Target> Targets() {
#ifdef LANTOSS_EXPLORER_TEST
    return testTargets;
#else
    std::vector<Target> result;
    HKEY peers;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\Lantoss\\Desktop\\Peers", 0, KEY_READ, &peers) != ERROR_SUCCESS) return result;
    for (DWORD i = 0; i < 32; ++i) {
        wchar_t key[65]{}; DWORD length = 65;
        if (RegEnumKeyExW(peers, i, key, &length, nullptr, nullptr, nullptr, nullptr) != ERROR_SUCCESS) break;
        if (!HexKey(key)) continue;
        wchar_t name[129]{}; DWORD bytes = sizeof(name);
        if (RegGetValueW(peers, key, L"Name", RRF_RT_REG_SZ, nullptr, name, &bytes) != ERROR_SUCCESS) continue;
        std::wstring label;
        for (wchar_t c : std::wstring(name)) if (c >= L' ' && label.size() < 64) { label += c; if (c == L'&') label += c; }
        result.push_back({label.empty() ? L"Windows PC" : label, key});
    }
    RegCloseKey(peers);
    std::sort(result.begin(), result.end(), [](const Target& a, const Target& b) { return a.name < b.name; });
    return result;
#endif
}
static std::wstring Executable() {
#ifdef LANTOSS_EXPLORER_TEST
    return testExecutable;
#else
    wchar_t path[32768]; DWORD count = GetModuleFileNameW(module, path, 32768);
    if (!count || count >= 32768) return {};
    std::wstring result(path, count);
    auto slash = result.find_last_of(L"\\/");
    return slash == std::wstring::npos ? L"" : result.substr(0, slash + 1) + L"Lantoss.exe";
#endif
}
static HRESULT Selection(IShellItemArray* items, std::vector<std::wstring>& paths) {
    if (!items) return E_INVALIDARG;
    DWORD count = 0; HRESULT hr = items->GetCount(&count);
    if (FAILED(hr) || !count || count > 256) return E_INVALIDARG;
    size_t bytes = 16;
    for (DWORD i = 0; i < count; ++i) {
        IShellItem* item = nullptr; hr = items->GetItemAt(i, &item);
        if (FAILED(hr)) return hr;
        PWSTR name = nullptr; hr = item->GetDisplayName(SIGDN_FILESYSPATH, &name); item->Release();
        if (FAILED(hr)) return hr;
        std::wstring path(name); CoTaskMemFree(name);
        if (path.empty() || path.size() > 32760 || std::any_of(path.begin(), path.end(), [](wchar_t c) { return c < 32; })) return E_INVALIDARG;
        bytes += 4 + path.size() * sizeof(wchar_t);
        if (bytes + 128 > 98304) return HRESULT_FROM_WIN32(ERROR_BUFFER_OVERFLOW);
        paths.push_back(std::move(path));
    }
    return S_OK;
}
static HRESULT RequestPath(std::wstring& path) {
#ifdef LANTOSS_EXPLORER_TEST
    path = testRequest; return S_OK;
#else
    PWSTR local = nullptr; HRESULT hr = SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_DEFAULT, nullptr, &local);
    if (FAILED(hr)) return hr;
    std::wstring root(local); CoTaskMemFree(local);
    // Refuse junctions in our two owned directories; inherit the user's LocalAppData ACL.
    for (const wchar_t* part : {L"\\Lantoss", L"\\ShellRequests"}) {
        root += part;
        if (!CreateDirectoryW(root.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS) return HRESULT_FROM_WIN32(GetLastError());
        DWORD attributes = GetFileAttributesW(root.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES || !(attributes & FILE_ATTRIBUTE_DIRECTORY) || (attributes & FILE_ATTRIBUTE_REPARSE_POINT)) return E_ACCESSDENIED;
    }
    GUID id; hr = CoCreateGuid(&id); if (FAILED(hr)) return hr;
    wchar_t guid[40]; StringFromGUID2(id, guid, 40);
    path = root + L"\\" + std::wstring(guid + 1, 36) + L".req"; return S_OK;
#endif
}
static HRESULT Launch(IShellItemArray* items, const std::wstring& key) {
    try {
        std::vector<std::wstring> paths; HRESULT hr = Selection(items, paths); if (FAILED(hr)) return hr;
        if (!key.empty() && !HexKey(key)) return E_INVALIDARG;
        std::wstring request; hr = RequestPath(request); if (FAILED(hr)) return hr;
        std::wstring exe = Executable(); if (exe.empty()) return E_FAIL;
        // Binary length-prefixed UTF-16 avoids command-line limits and quoting ambiguities.
        HANDLE file = CreateFileW(request.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
        bool ok = true;
        auto write = [&](const void* data, DWORD size) { DWORD written = 0; if (!WriteFile(file, data, size, &written, nullptr) || written != size) ok = false; };
        write("LTSSREQ1", 8);
        DWORD keyBytes = static_cast<DWORD>(key.size() * 2), count = static_cast<DWORD>(paths.size());
        write(&keyBytes, 4); write(key.data(), keyBytes); write(&count, 4);
        for (const auto& path : paths) { DWORD size = static_cast<DWORD>(path.size() * 2); write(&size, 4); write(path.data(), size); }
        CloseHandle(file);
        if (!ok) { DeleteFileW(request.c_str()); return E_FAIL; }
#ifdef LANTOSS_EXPLORER_TEST
        return S_OK; // The test harness verifies this file without launching the desktop app.
#else
        std::wstring command = L"\"" + exe + L"\" --shell-request \"" + request + L"\"";
        STARTUPINFOW startup{}; startup.cb = sizeof(startup); PROCESS_INFORMATION process{};
        if (!CreateProcessW(exe.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &process)) {
            DWORD error = GetLastError(); DeleteFileW(request.c_str()); return HRESULT_FROM_WIN32(error);
        }
        CloseHandle(process.hThread); CloseHandle(process.hProcess); return S_OK;
#endif
    } catch (...) { return E_OUTOFMEMORY; }
}

class Command;
class Enumeration final : public IEnumExplorerCommand {
    LONG refs = 1; std::vector<Target> targets; size_t position = 0;
public:
    explicit Enumeration(std::vector<Target> items, size_t at = 0) : targets(std::move(items)), position(at) { InterlockedIncrement(&objects); }
    ~Enumeration() { InterlockedDecrement(&objects); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (iid == IID_IUnknown || iid == IID_IEnumExplorerCommand) { *out = static_cast<IEnumExplorerCommand*>(this); AddRef(); return S_OK; } return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override { ULONG left = InterlockedDecrement(&refs); if (!left) delete this; return left; }
    HRESULT STDMETHODCALLTYPE Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override;
    HRESULT STDMETHODCALLTYPE Skip(ULONG count) override { size_t next = position + count; position = (std::min)(next, targets.size()); return next <= targets.size() ? S_OK : S_FALSE; }
    HRESULT STDMETHODCALLTYPE Reset() override { position = 0; return S_OK; }
    HRESULT STDMETHODCALLTYPE Clone(IEnumExplorerCommand** out) override {
        if (!out) return E_POINTER;
        *out = nullptr;
        try { *out = new Enumeration(targets, position); return S_OK; } catch (...) { return E_OUTOFMEMORY; }
    }
};
class Command final : public IExplorerCommand {
    LONG refs = 1; bool root; Target target;
public:
    explicit Command(bool parent = true, Target item = {}) : root(parent), target(std::move(item)) { InterlockedIncrement(&objects); }
    ~Command() { InterlockedDecrement(&objects); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (iid == IID_IUnknown || iid == IID_IExplorerCommand) { *out = static_cast<IExplorerCommand*>(this); AddRef(); return S_OK; } return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override { ULONG left = InterlockedDecrement(&refs); if (!left) delete this; return left; }
    HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray*, PWSTR* out) override { return SHStrDupW(root ? L"Share with Lantoss" : target.name.c_str(), out); }
    HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray*, PWSTR* out) override { if (!out) return E_POINTER; *out = nullptr; try { return SHStrDupW((Executable() + L",0").c_str(), out); } catch (...) { return E_OUTOFMEMORY; } }
    HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray*, PWSTR* out) override { if (!out) return E_POINTER; *out = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* out) override {
        if (!out) return E_POINTER;
        *out = CommandId;
        // Give each child a stable identity without persisting a device address in Explorer.
        if (!root && target.key.empty()) out->Data1 ^= 1;
        else if (!root && HexKey(target.key)) {
            auto hex = [](wchar_t c) { return c <= L'9' ? c - L'0' : (c | 32) - L'a' + 10; };
            auto bytes = reinterpret_cast<unsigned char*>(out);
            for (size_t i = 0; i < 16; ++i) bytes[i] ^= static_cast<unsigned char>((hex(target.key[i*2]) << 4) | hex(target.key[i*2+1]));
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override {
        if (!state) return E_POINTER;
        *state = ECS_HIDDEN;
        if (!Enabled()) return S_OK;
        DWORD count = 0;
        if (items && SUCCEEDED(items->GetCount(&count)) && count > 0 && count <= 256) {
            SFGAOF attributes = 0;
            if (SUCCEEDED(items->GetAttributes(SIATTRIBFLAGS_AND, SFGAO_FILESYSTEM, &attributes)) && (attributes & SFGAO_FILESYSTEM)) *state = ECS_ENABLED;
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray* items, IBindCtx*) override { return Enabled() ? Launch(items, target.key) : E_ACCESSDENIED; }
    HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* out) override { if (!out) return E_POINTER; *out = root ? ECF_HASSUBCOMMANDS : ECF_DEFAULT; return S_OK; }
    HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** out) override {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (!root) return E_NOTIMPL;
        try { auto targets = Targets(); targets.insert(targets.begin(), {L"Choose a PC...", L""}); *out = new Enumeration(std::move(targets)); return S_OK; } catch (...) { return E_OUTOFMEMORY; }
    }
};
HRESULT Enumeration::Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) {
    if (!commands || (!fetched && count != 1)) return E_POINTER;
    if (fetched) *fetched = 0;
    ULONG copied = 0;
    try {
        while (copied < count && position < targets.size()) { commands[copied++] = new Command(false, targets[position++]); }
    } catch (...) { for (ULONG i = 0; i < copied; ++i) { commands[i]->Release(); commands[i] = nullptr; } return E_OUTOFMEMORY; }
    if (fetched) *fetched = copied;
    return copied == count ? S_OK : S_FALSE;
}
class Factory final : public IClassFactory {
    LONG refs = 1;
public:
    Factory() { InterlockedIncrement(&objects); } ~Factory() { InterlockedDecrement(&objects); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override { if (!out) return E_POINTER; *out = nullptr; if (iid == IID_IUnknown || iid == IID_IClassFactory) { *out = static_cast<IClassFactory*>(this); AddRef(); return S_OK; } return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override { ULONG left = InterlockedDecrement(&refs); if (!left) delete this; return left; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** out) override {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        Command* command = new(std::nothrow) Command; if (!command) return E_OUTOFMEMORY;
        HRESULT hr = command->QueryInterface(iid, out); command->Release(); return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override { if (lock) InterlockedIncrement(&locks); else InterlockedDecrement(&locks); return S_OK; }
};
extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** out) {
    if (!out) return E_POINTER;
    *out = nullptr;
    if (clsid != CommandId) return CLASS_E_CLASSNOTAVAILABLE;
    Factory* factory = new(std::nothrow) Factory; if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(iid, out); factory->Release(); return hr;
}
extern "C" HRESULT __stdcall DllCanUnloadNow() { return objects == 0 && locks == 0 ? S_OK : S_FALSE; }
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) { if (reason == DLL_PROCESS_ATTACH) { module = instance; DisableThreadLibraryCalls(instance); } return TRUE; }
