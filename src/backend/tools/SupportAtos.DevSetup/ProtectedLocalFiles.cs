using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace SupportAtos.DevSetup;

[SupportedOSPlatform("windows")]
public static class ProtectedLocalFiles
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SupportAtos", "setup-private");

    public static void PrivateDirectory(string path)
    {
        var directory = Directory.CreateDirectory(path);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new SetupFailure("Diretorio privado nao pode ser link. Nenhum segredo foi gravado.");
        var security = new DirectorySecurity();
        var user = WindowsIdentity.GetCurrent().User!;
        security.SetOwner(user);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    public static void Write(string path, byte[] plaintext)
    {
        PrivateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(encrypted);
        file.Flush(true);
    }

    public static byte[] Read(string path)
        => ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
}
