// NetCafe tiny launcher, x64 Windows. The Unity runtime stays beside its _Data directory.
// No C runtime or networking. The active release pointer is an ASCII relative path in Builds.
// SPDX-License-Identifier: MIT
typedef unsigned short wchar_t;
typedef unsigned long DWORD;
typedef unsigned int UINT;
typedef void *HANDLE;
typedef void *HINSTANCE;
typedef void *HWND;
typedef long long INTPTR;
typedef int BOOL;
__declspec(dllimport) DWORD __stdcall GetModuleFileNameW(void*, wchar_t*, DWORD);
__declspec(dllimport) DWORD __stdcall GetFileAttributesW(const wchar_t*);
__declspec(dllimport) HANDLE __stdcall CreateFileW(const wchar_t*, DWORD, DWORD, void*, DWORD, DWORD, HANDLE);
__declspec(dllimport) BOOL __stdcall ReadFile(HANDLE, void*, DWORD, DWORD*, void*);
__declspec(dllimport) BOOL __stdcall CloseHandle(HANDLE);
__declspec(dllimport) __declspec(noreturn) void __stdcall ExitProcess(UINT);
__declspec(dllimport) HINSTANCE __stdcall ShellExecuteW(HWND,const wchar_t*,const wchar_t*,const wchar_t*,const wchar_t*,int);
__declspec(dllimport) int __stdcall MessageBoxW(HWND,const wchar_t*,const wchar_t*,UINT);
#define LIMIT 2048
static wchar_t root[LIMIT], target[LIMIT], wd[LIMIT], pointer_file[LIMIT];
static wchar_t rel[256];
static const wchar_t default_rel[] = L"Builds\\LibraryLounge-Cinema-Redesign-20261031\\LibraryLounge.exe";
static const wchar_t pointer_rel[] = L"Builds\\active-client.txt";
static int wlen(const wchar_t* a){int n=0;while(a[n])n++;return n;}
static void wcopy(wchar_t* dst,const wchar_t* src){int i=0;for(;src[i] && i<LIMIT-1;i++)dst[i]=src[i];dst[i]=0;}
static int append(wchar_t* dst,int cap,const wchar_t* src){int p=wlen(dst),i=0;if(p>=cap)return 0;while(src[i]){if(p+i>=cap-1)return 0;dst[p+i]=src[i];i++;}dst[p+i]=0;return 1;}
static int is_valid_rel(const wchar_t* s){
    const wchar_t prefix[]=L"Builds\\";
    const wchar_t ending[]=L"\\LibraryLounge.exe";
    int size=wlen(s),prefix_len=wlen(prefix),suffix_len=wlen(ending);
    if(size<prefix_len+suffix_len+1||size>=250)return 0;
    for(int i=0;i<prefix_len;i++)if(s[i]!=prefix[i])return 0;
    for(int i=0;i<suffix_len;i++)if(s[size-suffix_len+i]!=ending[i])return 0;
    for(int i=0;i<size;i++){
        wchar_t c=s[i];if(c==L'.'&&s[i+1]==L'.')return 0;
        if(!((c>=L'a'&&c<=L'z')||(c>=L'A'&&c<=L'Z')||(c>=L'0'&&c<=L'9')||c==L'_'||c==L'-'||c==L'.'||c==L'\\'||c==L'/'))return 0;
    }
    return 1;
}
static void read_active_pointer(){
    wcopy(pointer_file,root);if(!append(pointer_file,LIMIT,pointer_rel))return;
    HANDLE h=CreateFileW(pointer_file,0x80000000u,1,0,3,0,0);
    if(h==(HANDLE)-1)return;
    char data[255];DWORD got=0;BOOL ok=ReadFile(h,data,254,&got,0);CloseHandle(h);
    if(!ok||got<18||got>=254)return;
    int n=0;
    for(DWORD i=0;i<got&&n<250;i++){
        unsigned char c=(unsigned char)data[i];
        if(c=='\r'||c=='\n')break;
        if(c<33||c>126)return;
        rel[n++]=(wchar_t)c;
    }
    rel[n]=0;
    if(!is_valid_rel(rel))wcopy(rel,default_rel);
}
static void alert(const wchar_t* text){MessageBoxW(0,text,L"客厅网吧 · 游戏启动",0x10u);}
void mainCRTStartup(void){
    DWORD n=GetModuleFileNameW(0,root,LIMIT);
    if(!n||n>=LIMIT-256){alert(L"无法确定启动器位置。请从项目根目录运行。");ExitProcess(1);}
    while(n&&root[n-1]!=L'\\'&&root[n-1]!=L'/')n--;
    if(!n){alert(L"启动路径无效。");ExitProcess(2);}
    root[n]=0;
    wcopy(rel,default_rel);
    read_active_pointer();
    wcopy(target,root);
    if(!append(target,LIMIT,rel)){alert(L"游戏路径太长。");ExitProcess(3);}
    DWORD attr=GetFileAttributesW(target);
    if(attr==0xffffffffu||(attr&0x10)){
        alert(L"找不到完整游戏客户端。请检查 Builds 文件夹，或者查看 使用说明.html。\n启动器不会单独移动 Unity 的 Data/DLL 文件。");
        ExitProcess(4);
    }
    wcopy(wd,target);
    int len=wlen(wd);while(len&&wd[len-1]!=L'\\'&&wd[len-1]!=L'/')len--;
    wd[len]=0;
    INTPTR status=(INTPTR)ShellExecuteW(0,L"open",target,0,wd,1);
    if(status<=32){alert(L"无法启动游戏。请确认客户端完整，且未被系统安全软件阻止。");ExitProcess(5);}
    ExitProcess(0);
}
