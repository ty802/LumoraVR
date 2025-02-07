using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Aquamarine.Source.Logging;

namespace Aquamarine.Source.Management;

public partial class LocalDatabase : Node
{
    public static LocalDatabase Instance;
    private const string DATABASE_FILENAME = "userdata.db";
    private const string LOCK_FILENAME = "userdata.lock";
    private string _databasePath;
    private string _lockFilePath;
    private FileStream _lockFile;
    private Dictionary<string, object> _cache = new();
    private byte[] _encryptionKey;
    private byte[] _iv;

    public override void _Ready()
    {
        Instance = this;
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        try
        {
            // Set up paths
            var basePath = Path.Combine(OS.GetUserDataDir(), "LumoraVR");
            _databasePath = Path.Combine(basePath, DATABASE_FILENAME);
            _lockFilePath = Path.Combine(basePath, LOCK_FILENAME);

            // Create directory if it doesn't exist
            Directory.CreateDirectory(basePath);

            // Check for existing lock
            if (File.Exists(_lockFilePath))
            {
                try
                {
                    // Try to open the lock file
                    var existingLock = File.Open(_lockFilePath, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None);
                    existingLock.Close();
                    File.Delete(_lockFilePath);
                }
                catch (IOException)
                {
                    Logger.Error("Another instance of LumoraVR is running. Multiple instances are not supported.");
                    GetTree().Quit();
                    return;
                }
            }

            _lockFile = File.Open(_lockFilePath, FileMode.Create, System.IO.FileAccess.ReadWrite, FileShare.None);
            _lockFile.Write(Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString()));
            _lockFile.Flush();

            GenerateEncryptionKey();

            // Load database
            LoadDatabase();

            Logger.Log("Local database initialized successfully");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to initialize database: {ex.Message}");
            throw;
        }
    }

    private void GenerateEncryptionKey()
    {
        // Get unique machine identifiers
        var machineId = OS.GetUniqueId();
        var machineGuid = OS.GetProcessId(); // Additional entropy

        using var deriveBytes = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(machineId),
            Encoding.UTF8.GetBytes($"LumoraVR_{machineGuid}"),
            10000,
            HashAlgorithmName.SHA256);

        _encryptionKey = deriveBytes.GetBytes(32);
        _iv = deriveBytes.GetBytes(16); 
    }

    private void LoadDatabase()
    {
        try
        {
            if (!File.Exists(_databasePath))
            {
                _cache = new Dictionary<string, object>();
                SaveDatabase();
                return;
            }

            var encryptedData = File.ReadAllBytes(_databasePath);
            var jsonString = DecryptData(encryptedData);
            _cache = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString) 
                    ?? new Dictionary<string, object>();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load database: {ex.Message}");
            _cache = new Dictionary<string, object>();
            SaveDatabase();
        }
    }

    private void SaveDatabase()
    {
        try
        {
            var jsonString = JsonSerializer.Serialize(_cache);
            var encryptedData = EncryptData(jsonString);
            File.WriteAllBytes(_databasePath, encryptedData);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save database: {ex.Message}");
            throw;
        }
    }

    private byte[] EncryptData(string data)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.IV = _iv;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(data);
        return encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
    }

    private string DecryptData(byte[] encryptedData)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.IV = _iv;

        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
        return Encoding.UTF8.GetString(decryptedBytes);
    }

    public T GetValue<T>(string key, T defaultValue = default)
    {
        try
        {
            if (_cache.TryGetValue(key, out var value))
            {
                if (value is JsonElement jsonElement)
                {
                    return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
                }
                return (T)value;
            }
            return defaultValue;
        }
        catch (Exception ex)
        {
            Logger.Error($"Error retrieving value for key {key}: {ex.Message}");
            return defaultValue;
        }
    }

    public void SetValue<T>(string key, T value)
    {
        try
        {
            _cache[key] = value;
            SaveDatabase();
        }
        catch (Exception ex)
        {
            Logger.Error($"Error setting value for key {key}: {ex.Message}");
            throw;
        }
    }

    public void DeleteValue(string key)
    {
        try
        {
            if (_cache.Remove(key))
            {
                SaveDatabase();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error deleting value for key {key}: {ex.Message}");
            throw;
        }
    }

    public bool HasKey(string key)
    {
        return _cache.ContainsKey(key);
    }

    public void ClearDatabase()
    {
        try
        {
            _cache.Clear();
            SaveDatabase();
        }
        catch (Exception ex)
        {
            Logger.Error($"Error clearing database: {ex.Message}");
            throw;
        }
    }

    public override void _ExitTree()
    {
        try
        {
            SaveDatabase();
            _lockFile?.Close();
            if (File.Exists(_lockFilePath))
            {
                File.Delete(_lockFilePath);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error during database cleanup: {ex.Message}");
        }
    }
}