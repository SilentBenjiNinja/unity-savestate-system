using System;
using System.IO;
using System.Text;
using Sirenix.OdinInspector;
using UnityEngine;
using Debug = UnityEngine.Debug;

/*
 * TODO:
 * Backup strategy
 * File validation
 * Async operations
 * Progress feedback
 * Thread safety
 * Error handling
 * 
 * Provide Samples
 * Tests?
 */

namespace bnj.savestate_system.Runtime
{
    /// <summary>
    /// Handles file I/O operations for savestate persistence with validation, backup, and migration support.
    /// </summary>
    [Serializable]
    public class SavestateFileIOStreamer<T> where T : ISerializableSavestate
    {
        #region Inspector Configuration
        [SerializeField, ToggleLeft]
        private bool _useTestSavestate;

        [FoldoutGroup("Test Savestate"), ShowIf("_useTestSavestate")]
        [SerializeField, HideLabel]
        private T _testSavestate;

        [FoldoutGroup("Fallback Savestate")]
        [SerializeField, HideLabel]
        private T _fallbackSavestate;

        [Tooltip("Create debug JSON when saving & print to console.")]
        [SerializeField, ToggleLeft]
        private bool _debugMode = true;

        [Tooltip("Number of backup files to keep (0 = disabled)")]
        [SerializeField, Range(0, 5)]
        private int _backupCount = 2;

        [Tooltip("Validate file integrity before loading")]
        [SerializeField, ToggleLeft]
        private bool _validateFiles = true;
        #endregion

        #region Dependencies
        private ISerializer<T> _serializer;
        private ISavestateMigrator<T> _migrator;
        #endregion

        #region Constants
        private const string MAGIC_HEADER = "SAVE"; // 4-byte identifier
        private const int HEADER_SIZE = 8; // Magic(4) + Version(4)
        #endregion

        #region Paths
#if UNITY_INCLUDE_TESTS

        private string _customFolderPath;
        protected string FolderPath => _customFolderPath ?? Application.persistentDataPath;
#else
        protected string FolderPath => Application.persistentDataPath;
#endif
        protected string FilePath => Path.Combine(FolderPath, "savestate.sav");
        protected string DebugFilePath => Path.Combine(FolderPath, "savestate_debug.json");
        protected string BackupPath(int index) => Path.Combine(FolderPath, $"savestate.backup{index}.sav");
        #endregion

        #region Initialization
        /// <summary>
        /// Initialize with required serializer.
        /// </summary>
        public void Init(ISerializer<T> serializer)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            EnsureDirectoryExists();
        }

        /// <summary>
        /// Initialize with serializer and optional migrator for version handling.
        /// </summary>
        public void Init(ISerializer<T> serializer, ISavestateMigrator<T> migrator)
        {
            Init(serializer);
            _migrator = migrator;
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(FolderPath))
            {
                Directory.CreateDirectory(FolderPath);
            }
        }
        #endregion

        #region Loading
        /// <summary>
        /// Loads savestate from file. Returns test/fallback savestate if configured or file doesn't exist.
        /// Automatically migrates if version mismatch detected.
        /// </summary>
        public T LoadFromFile()
        {
            // Test savestate bypass
            if (_useTestSavestate)
            {
                Debug.Log("Using test savestate (file I/O bypassed).");
                return _testSavestate;
            }

            // No file exists
            if (!File.Exists(FilePath))
            {
                Debug.Log("No savestate file found. Using fallback.");
                return _fallbackSavestate;
            }

            try
            {
                // Read file
                var fileBytes = File.ReadAllBytes(FilePath);

                // Validate file
                if (_validateFiles && !ValidateFile(fileBytes, out var errorMessage))
                {
                    Debug.LogError($"File validation failed: {errorMessage}. Attempting backup restore...");
                    return TryLoadFromBackup() ?? _fallbackSavestate;
                }

                // Extract payload (skip header if validation is enabled)
                var payload = _validateFiles ? ExtractPayload(fileBytes) : fileBytes;

                // Deserialize
                var savestate = _serializer.Deserialize(payload);

                if (_debugMode)
                {
                    Debug.Log($"Loaded {fileBytes.Length} bytes:\n{_serializer.ConvertToJson(payload)}");
                }
                else
                {
                    Debug.Log($"Loaded {fileBytes.Length} bytes from savestate.");
                }

                // Attempt migration if migrator is configured
                if (_migrator != null)
                {
                    savestate = TryMigrateSavestate(savestate);
                }

                return savestate;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load savestate: {ex.Message}. Using fallback.");
                return TryLoadFromBackup() ?? _fallbackSavestate;
            }
        }

        private T TryMigrateSavestate(T savestate)
        {
            if (savestate.Version == _migrator.CurrentVersion)
            {
                Debug.Log($"Savestate v{savestate.Version} is current. No migration needed.");
                return savestate;
            }

            Debug.Log($"Version mismatch. Found: v{savestate.Version}, Expected: v{_migrator.CurrentVersion}");

            // Create backup before migration
            if (_backupCount > 0)
            {
                CreateBackup();
            }

            // Attempt migration
            if (_migrator.TryMigrate(savestate, out var migratedSavestate))
            {
                Debug.Log($"Successfully migrated v{savestate.Version} → v{migratedSavestate.Version}");

                // Auto-save migrated version
                SaveToFile(migratedSavestate);

                return migratedSavestate;
            }
            else
            {
                Debug.LogError("Migration failed. Using fallback savestate.");
                return _fallbackSavestate;
            }
        }

        private T TryLoadFromBackup()
        {
            for (int i = 0; i < _backupCount; i++)
            {
                var backupPath = BackupPath(i);
                if (!File.Exists(backupPath))
                    continue;

                try
                {
                    Debug.Log($"Attempting to restore from backup {i}...");

                    var backupBytes = File.ReadAllBytes(backupPath);

                    // Validate backup
                    if (_validateFiles && !ValidateFile(backupBytes, out var errorMessage))
                    {
                        Debug.LogWarning($"Backup {i} validation failed: {errorMessage}");
                        continue;
                    }

                    var payload = _validateFiles ? ExtractPayload(backupBytes) : backupBytes;
                    var savestate = _serializer.Deserialize(payload);

                    Debug.Log($"Successfully restored from backup {i}.");
                    return savestate;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Backup {i} failed: {ex.Message}");
                }
            }

            return default;
        }
        #endregion

        #region Saving
        /// <summary>
        /// Saves savestate to file. Skips if IsDirty is false.
        /// Automatically creates backups if configured.
        /// </summary>
        public void SaveToFile(T savestate)
        {
            if (savestate == null)
            {
                throw new ArgumentNullException(nameof(savestate));
            }

            // Skip if not dirty
            if (!savestate.IsDirty)
            {
                Debug.Log("Savestate unchanged. Skipping save.");
                return;
            }

            try
            {
                // Serialize payload
                var payload = _serializer.Serialize(savestate);

                // Wrap with validation header if enabled
                var fileBytes = _validateFiles ? WrapWithHeader(payload, savestate.Version) : payload;

                // Rotate backups
                if (_backupCount > 0)
                {
                    RotateBackups();
                }

                // Write main file
                File.WriteAllBytes(FilePath, fileBytes);

                // Debug JSON export
                if (_debugMode)
                {
                    WriteDebugJson(savestate);
                    Debug.Log($"Saved {fileBytes.Length} bytes:\n{_serializer.ConvertToJson(payload)}");
                }
                else
                {
                    Debug.Log($"Saved {fileBytes.Length} bytes to savestate.");
                }

                savestate.RemoveDirtyFlag();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save: {ex.Message}");
                throw;
            }
        }

        private void RotateBackups()
        {
            try
            {
                // Delete oldest backup
                var oldestBackup = BackupPath(_backupCount - 1);
                if (File.Exists(oldestBackup))
                {
                    File.Delete(oldestBackup);
                }

                // Shift backups down (backup0 → backup1, etc.)
                for (int i = _backupCount - 2; i >= 0; i--)
                {
                    var source = BackupPath(i);
                    if (File.Exists(source))
                    {
                        File.Move(source, BackupPath(i + 1));
                    }
                }

                // Copy current save as backup0
                if (File.Exists(FilePath))
                {
                    File.Copy(FilePath, BackupPath(0), overwrite: true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Backup rotation failed: {ex.Message}");
            }
        }

        private void WriteDebugJson(T savestate)
        {
            try
            {
                var json = JsonUtility.ToJson(savestate, prettyPrint: true);
                File.WriteAllText(DebugFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Debug JSON export failed: {ex.Message}");
            }
        }
        #endregion

        #region File Validation
        /// <summary>
        /// Validates file integrity.
        /// Format: [Magic:4][Version:4][Payload:N]
        /// </summary>
        private bool ValidateFile(byte[] fileBytes, out string errorMessage)
        {
            // Check minimum size
            if (fileBytes.Length < HEADER_SIZE)
            {
                errorMessage = $"File too small ({fileBytes.Length} bytes, expected at least {HEADER_SIZE})";
                return false;
            }

            // Check magic header
            var magic = Encoding.ASCII.GetString(fileBytes, 0, 4);
            if (magic != MAGIC_HEADER)
            {
                errorMessage = $"Invalid magic header: '{magic}' (expected '{MAGIC_HEADER}')";
                return false;
            }

            // Check version
            var version = BitConverter.ToInt32(fileBytes, 4);
            if (version < 1)
            {
                errorMessage = $"Invalid version: {version} (expected integer > 0)";
                return false;
            }

            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Wraps serialized payload with validation header.
        /// </summary>
        private byte[] WrapWithHeader(byte[] payload, int version)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // Write magic header
            writer.Write(Encoding.ASCII.GetBytes(MAGIC_HEADER));

            // Write version
            writer.Write(version);

            // Write payload
            writer.Write(payload);

            return ms.ToArray();
        }

        /// <summary>
        /// Extracts payload from wrapped file (skips header).
        /// </summary>
        private byte[] ExtractPayload(byte[] fileBytes)
        {
            var payload = new byte[fileBytes.Length - HEADER_SIZE];
            Array.Copy(fileBytes, HEADER_SIZE, payload, 0, payload.Length);
            return payload;
        }
        #endregion

        #region Utility
        /// <summary>
        /// Manually creates a backup of the current save file.
        /// </summary>
        public void CreateBackup()
        {
            if (!File.Exists(FilePath))
            {
                Debug.LogWarning("No savestate to backup.");
                return;
            }

            RotateBackups();
            Debug.Log("Manual backup created.");
        }

        /// <summary>
        /// Deletes all save files including backups. Useful for testing or account reset.
        /// </summary>
        public void DeleteAllSaves()
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);

            for (int i = 0; i < _backupCount; i++)
            {
                var backupPath = BackupPath(i);
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }

            if (File.Exists(DebugFilePath))
                File.Delete(DebugFilePath);

            Debug.Log("All save files deleted.");
        }
        #endregion

        #region Test Support
#if UNITY_INCLUDE_TESTS
        /// <summary>
        /// Test-friendly constructor for unit testing.
        /// </summary>
        internal SavestateFileIOStreamer(
            T fallbackSavestate,
            T testSavestate = default,
            bool useTestSavestate = false,
            bool debugMode = false,
            int backupCount = 0,
            bool validateFiles = true,
            string customFolderPath = null)
        {
            _fallbackSavestate = fallbackSavestate;
            _testSavestate = testSavestate;
            _useTestSavestate = useTestSavestate;
            _debugMode = debugMode;
            _backupCount = backupCount;
            _validateFiles = validateFiles;

            if (!string.IsNullOrEmpty(customFolderPath))
            {
                _customFolderPath = customFolderPath;
            }
        }
#endif
        #endregion
    }
}
