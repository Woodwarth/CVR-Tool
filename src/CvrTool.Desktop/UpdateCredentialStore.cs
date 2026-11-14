// Desktop / Private update credentials
// Store one GitHub read token per release repository in the user's OS vault.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CvrTool.Desktop;

internal static class UpdateCredentialStore
{
    private const string Service = "CVR Tool GitHub updates";
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const int ItemMissing = -25300;

    public static string? Read(string repository)
    {
        var account = repository.ToLowerInvariant();
        if (OperatingSystem.IsWindows())
        {
            if (!CredRead(Service + "/" + account, 1, 0, out var item))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1168) return null;
                throw new Win32Exception(error);
            }
            try
            {
                var credential = Marshal.PtrToStructure<Credential>(item);
                return Marshal.PtrToStringUTF8(credential.Blob, (int)credential.Size);
            }
            finally { CredFree(item); }
        }
        var status = Find(account, out var size, out var data, out var reference);
        if (status == ItemMissing) return null;
        Check(status);
        try { return Marshal.PtrToStringUTF8(data, (int)size); }
        finally { SecKeychainItemFreeContent(0, data); CFRelease(reference); }
    }

    public static void Write(string repository, string? token)
    {
        var account = repository.ToLowerInvariant();
        if (OperatingSystem.IsWindows() && token is null)
        {
            if (!CredDelete(Service + "/" + account, 1, 0) && Marshal.GetLastWin32Error() is var error && error != 1168)
                throw new Win32Exception(error);
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(token ?? "");
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var credential = new Credential { Type = 1, Target = Service + "/" + account, Size = (uint)bytes.Length,
                    Blob = pin.AddrOfPinnedObject(), Persist = 2, User = account };
                if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return;
            }
            var status = Find(account, out _, out var oldData, out var reference);
            if (status == ItemMissing)
            {
                if (token is not null)
                    Check(SecKeychainAddGenericPassword(0, Service.Length, Service, account.Length, account, bytes.Length, pin.AddrOfPinnedObject(), 0));
                return;
            }
            Check(status);
            try
            {
                SecKeychainItemFreeContent(0, oldData);
                Check(token is null ? SecKeychainItemDelete(reference) :
                    SecKeychainItemModifyAttributesAndData(reference, 0, bytes.Length, pin.AddrOfPinnedObject()));
            }
            finally { CFRelease(reference); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); pin.Free(); }
    }

    private static int Find(string account, out uint size, out nint data, out nint item)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        return SecKeychainFindGenericPassword(0, Service.Length, Service, account.Length, account, out size, out data, out item);
    }

    private static void Check(int status)
    {
        if (status != 0) throw new InvalidOperationException($"Keychain access failed ({status}).");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string? Target, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint Size;
        public nint Blob;
        public uint Persist, AttributeCount;
        public nint Attributes, Alias;
        public string? User;
    }

    [DllImport("advapi32", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out nint result);
    [DllImport("advapi32", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32")] private static extern void CredFree(nint buffer);
    [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(nint chain, int sl, string service, int al, string account, out uint size, out nint data, out nint item);
    [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(nint chain, int sl, string service, int al, string account, int size, nint data, nint item);
    [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(nint item, nint attributes, int size, nint data);
    [DllImport(Security)] private static extern int SecKeychainItemFreeContent(nint attributes, nint data);
    [DllImport(Security)] private static extern int SecKeychainItemDelete(nint item);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(nint item);
}
