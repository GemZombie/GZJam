using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace GZJam.Library.Servers;

/// <summary>
/// Windows Credential Manager (Control Panel › Credential Manager › Windows Credentials), as a
/// generic credential named "GZJam/&lt;user&gt;@&lt;address&gt;". Kept for this user on this computer.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsCredentials : PasswordStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    private static string Target(string key) => "GZJam/" + key;

    public override string Where => "Windows Credential Manager";

    public override string? Get(string key)
    {
        if (!CredReadW(Target(key), CredTypeGeneric, 0, out IntPtr found))
        {
            int error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? null : throw new Win32Exception(error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(found);
            return credential.CredentialBlobSize == 0
                ? ""
                : Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(found);
        }
    }

    public override void Set(string key, string password)
    {
        byte[] blob = Encoding.Unicode.GetBytes(password);
        IntPtr target = Marshal.StringToCoTaskMemUni(Target(key));
        IntPtr user = Marshal.StringToCoTaskMemUni(key[..Math.Max(0, key.LastIndexOf('@'))]);
        IntPtr data = Marshal.AllocCoTaskMem(Math.Max(1, blob.Length));
        try
        {
            Marshal.Copy(blob, 0, data, blob.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = blob.Length,
                CredentialBlob = data,
                Persist = CredPersistLocalMachine,
                UserName = user,
            };
            if (!CredWriteW(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally
        {
            Marshal.Copy(new byte[blob.Length], 0, data, blob.Length);   // the password, gone from memory GZJam owns
            Marshal.FreeCoTaskMem(data);
            Marshal.FreeCoTaskMem(target);
            Marshal.FreeCoTaskMem(user);
        }
    }

    public override void Remove(string key)
    {
        if (!CredDeleteW(Target(key), CredTypeGeneric, 0) && Marshal.GetLastPInvokeError() is var error and not ErrorNotFound)
            throw new Win32Exception(error);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref Credential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}

/// <summary>The macOS Keychain (Keychain Access › login), as a password for the service "GZJam".</summary>
[SupportedOSPlatform("macos")]
internal sealed class MacKeychain : PasswordStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ItemNotFound = -25300;
    private static readonly byte[] Service = Encoding.UTF8.GetBytes("GZJam");

    public override string Where => "the Keychain";

    public override string? Get(string key)
    {
        byte[] account = Encoding.UTF8.GetBytes(key);
        int status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
            out uint length, out IntPtr data, out IntPtr item);
        if (status == ItemNotFound)
            return null;
        Check(status);
        try
        {
            return length == 0 ? "" : Marshal.PtrToStringUTF8(data, (int)length);
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero)
                CFRelease(item);
        }
    }

    public override void Set(string key, string password)
    {
        byte[] account = Encoding.UTF8.GetBytes(key);
        byte[] secret = Encoding.UTF8.GetBytes(password);
        try
        {
            int status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
                out _, out IntPtr data, out IntPtr item);
            if (status == ItemNotFound)
            {
                Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
                    (uint)secret.Length, secret, IntPtr.Zero));
                return;
            }
            Check(status);
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            try
            {
                Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)secret.Length, secret));
            }
            finally
            {
                CFRelease(item);
            }
        }
        finally
        {
            Array.Clear(secret);
        }
    }

    public override void Remove(string key)
    {
        byte[] account = Encoding.UTF8.GetBytes(key);
        int status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
            out _, out IntPtr data, out IntPtr item);
        if (status == ItemNotFound)
            return;
        Check(status);
        SecKeychainItemFreeContent(IntPtr.Zero, data);
        try
        {
            Check(SecKeychainItemDelete(item));
        }
        finally
        {
            CFRelease(item);
        }
    }

    private static void Check(int status)
    {
        if (status != 0)
            throw new InvalidOperationException($"The Keychain said no (OSStatus {status}).");
    }

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service,
        uint accountLength, byte[] account, out uint passwordLength, out IntPtr password, out IntPtr item);

    [DllImport(Security)]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service,
        uint accountLength, byte[] account, uint passwordLength, byte[] password, IntPtr item);

    [DllImport(Security)]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, uint length, byte[] data);

    [DllImport(Security)]
    private static extern int SecKeychainItemDelete(IntPtr item);

    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr item);
}

/// <summary>
/// The freedesktop Secret Service (GNOME Keyring, KDE Wallet, KeePassXC), through libsecret, which
/// GNOME and KDE desktops both have. Passwords are found by a schema of GZJam's own and a "key"
/// attribute; Seahorse or KWalletManager shows them as "GZJam: &lt;user&gt;@&lt;address&gt;".
/// </summary>
internal sealed class LinuxSecretService : PasswordStore
{
    private const string LibSecret = "libsecret-1.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string SchemaName = "org.gzjam.ServerPassword";

    private readonly IntPtr _schema;
    private readonly IntPtr _strHash;
    private readonly IntPtr _strEqual;

    private LinuxSecretService(IntPtr schema, IntPtr strHash, IntPtr strEqual)
    {
        _schema = schema;
        _strHash = strHash;
        _strEqual = strEqual;
    }

    public override string Where => "the desktop's keychain";

    /// <summary>The Secret Service, if libsecret is installed and a service answers; else null.</summary>
    public static LinuxSecretService? TryOpen()
    {
        try
        {
            IntPtr glib = NativeLibrary.Load(GLib);
            var service = new LinuxSecretService(MakeSchema(), NativeLibrary.GetExport(glib, "g_str_hash"),
                NativeLibrary.GetExport(glib, "g_str_equal"));
            service.Get("gzjam-probe");   // throws if no Secret Service runs on this desktop
            return service;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// A SecretSchema, laid out as libsecret's header has it: the name, flags (none), then up to 32
    /// attributes of a name and a type (0, a string), and room libsecret keeps for itself. Made
    /// once and never freed: libsecret may hold on to it.
    /// </summary>
    private static IntPtr MakeSchema()
    {
        int size = 8 + 8 + 32 * 16 + 8 + 7 * 8;
        IntPtr schema = Marshal.AllocHGlobal(size);
        Marshal.Copy(new byte[size], 0, schema, size);
        Marshal.WriteIntPtr(schema, 0, Marshal.StringToHGlobalAnsi(SchemaName));
        Marshal.WriteIntPtr(schema, 16, Marshal.StringToHGlobalAnsi("key"));   // attributes[0].name, type 0: string
        return schema;
    }

    public override string? Get(string key) => WithAttributes(key, attributes =>
    {
        IntPtr found = secret_password_lookupv_sync(_schema, attributes, IntPtr.Zero, out IntPtr error);
        ThrowIf(error);
        if (found == IntPtr.Zero)
            return null;
        try
        {
            return Marshal.PtrToStringUTF8(found);
        }
        finally
        {
            secret_password_free(found);
        }
    });

    public override void Set(string key, string password) => WithAttributes(key, attributes =>
    {
        secret_password_storev_sync(_schema, attributes, null, "GZJam: " + key, password, IntPtr.Zero, out IntPtr error);
        ThrowIf(error);
        return (string?)null;
    });

    public override void Remove(string key) => WithAttributes(key, attributes =>
    {
        secret_password_clearv_sync(_schema, attributes, IntPtr.Zero, out IntPtr error);
        ThrowIf(error);
        return (string?)null;
    });

    /// <summary>Runs <paramref name="call"/> with a GHashTable of the one attribute, key = <paramref name="key"/>.</summary>
    private string? WithAttributes(string key, Func<IntPtr, string?> call)
    {
        IntPtr name = Marshal.StringToCoTaskMemUTF8("key");
        IntPtr value = Marshal.StringToCoTaskMemUTF8(key);
        IntPtr table = g_hash_table_new(_strHash, _strEqual);
        try
        {
            g_hash_table_insert(table, name, value);
            return call(table);
        }
        finally
        {
            g_hash_table_unref(table);
            Marshal.FreeCoTaskMem(name);
            Marshal.FreeCoTaskMem(value);
        }
    }

    /// <summary>A GError's message (it's a domain, a code, then the message), as an exception.</summary>
    private static void ThrowIf(IntPtr error)
    {
        if (error == IntPtr.Zero)
            return;
        string message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "unknown error";
        g_error_free(error);
        throw new InvalidOperationException("The desktop's keychain said no: " + message);
    }

    [DllImport(LibSecret)]
    private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

    [DllImport(LibSecret)]
    private static extern int secret_password_storev_sync(IntPtr schema, IntPtr attributes,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? collection, [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string password, IntPtr cancellable, out IntPtr error);

    [DllImport(LibSecret)]
    private static extern int secret_password_clearv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

    [DllImport(LibSecret)]
    private static extern void secret_password_free(IntPtr password);

    [DllImport(GLib)]
    private static extern IntPtr g_hash_table_new(IntPtr hash, IntPtr equal);

    [DllImport(GLib)]
    private static extern int g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);

    [DllImport(GLib)]
    private static extern void g_hash_table_unref(IntPtr table);

    [DllImport(GLib)]
    private static extern void g_error_free(IntPtr error);
}
