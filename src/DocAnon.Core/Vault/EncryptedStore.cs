using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocAnon.Core.Profiles;

namespace DocAnon.Core.Vault;

public sealed record StoredItem(string Path, string Label, string Id, DateTime ModifiedUtc, bool PasswordProtected);

/// <summary>Folder of encrypted files named "{label}-{id}.{extension}".</summary>
public class EncryptedStore
{
    public EncryptedStore(string folder, string extension)
    {
        Folder = folder;
        Extension = extension;
        Directory.CreateDirectory(folder);
    }

    public string Folder { get; }
    public string Extension { get; }

    public static string DefaultRoot()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "DocAnon");
    }

    public static string Slug(string label)
    {
        var sb = new StringBuilder();
        foreach (char c in label.Trim())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c is ' ' or '-' or '_' or '.') sb.Append('-');
        }
        string s = sb.ToString().Trim('-');
        return s.Length == 0 ? "item" : s.Length > 60 ? s[..60] : s;
    }

    public string PathFor(string label, string id) => System.IO.Path.Combine(Folder, $"{Slug(label)}-{id}.{Extension}");

    public IReadOnlyList<StoredItem> List()
    {
        var items = new List<StoredItem>();
        foreach (var f in Directory.EnumerateFiles(Folder, "*." + Extension))
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(f);
            int dash = name.LastIndexOf('-');
            string label = dash > 0 ? name[..dash] : name;
            string id = dash > 0 ? name[(dash + 1)..] : name;
            bool pwd;
            using (var fs = File.OpenRead(f))
            {
                var head = new byte[9];
                int read = fs.Read(head, 0, head.Length);
                pwd = read == 9 && VaultCrypto.IsPasswordProtected(head);
            }
            items.Add(new StoredItem(f, label, id, File.GetLastWriteTimeUtc(f), pwd));
        }
        return items.OrderByDescending(i => i.ModifiedUtc).ToList();
    }

    protected void WriteBytes(string path, byte[] plaintext, VaultKey key)
    {
        byte[] data = VaultCrypto.Encrypt(plaintext, key);
        CryptographicOperations.ZeroMemory(plaintext);
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }

    protected static byte[] ReadBytes(string path, VaultKey key) => VaultCrypto.Decrypt(File.ReadAllBytes(path), key);

    /// <summary>
    /// Overwrites the file with random bytes before deleting it. On SSDs the old blocks may
    /// survive in the drive's spare area, so full-disk encryption is still recommended.
    /// </summary>
    public static void SecureDelete(string path)
    {
        if (!File.Exists(path)) return;
        long len = new FileInfo(path).Length;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            var buf = new byte[8192];
            for (long done = 0; done < len; done += buf.Length)
            {
                RandomNumberGenerator.Fill(buf);
                fs.Write(buf, 0, (int)Math.Min(buf.Length, len - done));
            }
            fs.Flush(true);
        }
        File.Delete(path);
    }
}

public sealed class SessionStore : EncryptedStore
{
    public SessionStore(string? root = null) : base(System.IO.Path.Combine(root ?? DefaultRoot(), "sessions"), "docanon") { }

    public string Save(Session s, VaultKey key)
    {
        string path = PathFor(s.Label, s.Id);
        WriteBytes(path, s.Serialize(), key);
        return path;
    }

    public Session Load(string path, VaultKey key)
    {
        byte[] plain = ReadBytes(path, key);
        try
        {
            return Session.Deserialize(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}

public sealed class ProfileStore : EncryptedStore
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() }, WriteIndented = false };

    public ProfileStore(string? root = null) : base(System.IO.Path.Combine(root ?? DefaultRoot(), "profiles"), "docprofile") { }

    public string Save(CompanyProfile p, VaultKey key)
    {
        string path = PathFor(p.Name, p.Id);
        // A renamed profile keeps its id, so the old file is removed.
        foreach (var old in List().Where(i => i.Id == p.Id && i.Path != path)) SecureDelete(old.Path);
        WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(p, Json), key);
        return path;
    }

    public CompanyProfile Load(string path, VaultKey key)
    {
        byte[] plain = ReadBytes(path, key);
        try
        {
            return JsonSerializer.Deserialize<CompanyProfile>(plain, Json) ?? new CompanyProfile();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
