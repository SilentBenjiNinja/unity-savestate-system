using System;
using System.IO;
using bnj.savestate_system.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace bnj.savestate_system.Tests
{
    [TestFixture]
    public class SavestateFileIOStreamerTests
    {
        private string _testDirectory;
        private TestSerializer _serializer;

        [SetUp]
        public void Setup()
        {
            _testDirectory = Path.Combine(Application.temporaryCachePath, "SavestateTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testDirectory);
            _serializer = new TestSerializer();
        }

        [TearDown]
        public void Teardown()
        {
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, true);
                }
                catch
                {
                    // Ignore cleanup errors in tests
                }
            }
        }

        #region Initialization Tests

        [Test]
        public void Init_WithNullSerializer_ThrowsArgumentNullException()
        {
            // Arrange
            var streamer = CreateStreamer();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => streamer.Init(null));
        }

        [Test]
        public void Init_WithValidSerializer_DoesNotThrow()
        {
            // Arrange
            var streamer = CreateStreamer();

            // Act & Assert
            Assert.DoesNotThrow(() => streamer.Init(_serializer));
        }

        [Test]
        public void Init_CreatesDirectoryIfNotExists()
        {
            // Arrange
            var customPath = Path.Combine(_testDirectory, "NewFolder");
            var streamer = CreateStreamer(customFolderPath: customPath);

            // Act
            streamer.Init(_serializer);

            // Assert
            Assert.IsTrue(Directory.Exists(customPath));
        }

        #endregion

        #region Loading Tests

        [Test]
        public void LoadFromFile_WhenFileDoesNotExist_ReturnsFallbackSavestate()
        {
            // Arrange
            var fallback = new TestSavestate { Version = 1, TestData = "fallback" };
            var streamer = CreateStreamer(fallback);
            streamer.Init(_serializer);

            // Act
            var result = streamer.LoadFromFile();

            // Assert
            Assert.AreEqual(fallback.TestData, result.TestData);
        }

        [Test]
        public void LoadFromFile_WithTestSavestateEnabled_ReturnsTestSavestate()
        {
            // Arrange
            var fallback = new TestSavestate { Version = 1, TestData = "fallback" };
            var testSavestate = new TestSavestate { Version = 1, TestData = "test" };
            var streamer = CreateStreamer(fallback, testSavestate, useTestSavestate: true);
            streamer.Init(_serializer);

            // Act
            var result = streamer.LoadFromFile();

            // Assert
            Assert.AreEqual("test", result.TestData);
        }

        [Test]
        public void LoadFromFile_WithValidFile_ReturnsDeserializedSavestate()
        {
            // Arrange
            var expected = new TestSavestate { Version = 1, TestData = "saved_data", IsDirty = true };
            var streamer = CreateStreamer();
            streamer.Init(_serializer);

            // Create a valid save file
            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            var payload = _serializer.Serialize(expected);
            var wrapped = WrapWithHeader(payload, expected.Version);
            File.WriteAllBytes(filePath, wrapped);

            // Act
            var result = streamer.LoadFromFile();

            // Assert
            Assert.AreEqual(expected.TestData, result.TestData);
            Assert.AreEqual(expected.Version, result.Version);
        }

        [Test]
        public void LoadFromFile_WithInvalidMagicHeader_ReturnsFallback()
        {
            // Arrange
            var fallback = new TestSavestate { Version = 1, TestData = "fallback" };
            var streamer = CreateStreamer(fallback);
            streamer.Init(_serializer);

            // Create invalid file
            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            File.WriteAllBytes(filePath, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0x00, 0x00, 0x00 });

            // Act
            var result = streamer.LoadFromFile();

            // Assert
            Assert.AreEqual(fallback.TestData, result.TestData);
        }

        [Test]
        public void LoadFromFile_WithCorruptedFile_TriesBackup()
        {
            // Arrange
            var fallback = new TestSavestate { Version = 1, TestData = "fallback" };
            var backupData = new TestSavestate { Version = 1, TestData = "backup_data" };
            var streamer = CreateStreamer(fallback, backupCount: 2);
            streamer.Init(_serializer);

            // Create corrupted main file
            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            File.WriteAllBytes(filePath, new byte[] { 0xFF, 0xFF });

            // Create valid backup
            var backupPath = Path.Combine(_testDirectory, "savestate.backup0.sav");
            var payload = _serializer.Serialize(backupData);
            var wrapped = WrapWithHeader(payload, backupData.Version);
            File.WriteAllBytes(backupPath, wrapped);

            // Act
            var result = streamer.LoadFromFile();

            // Assert
            Assert.AreEqual("backup_data", result.TestData);
        }

        [Test]
        public void LoadFromFile_WithValidationDisabled_SkipsValidation()
        {
            // Arrange
            var expected = new TestSavestate { Version = 1, TestData = "raw_data" };
            var streamer = CreateStreamer(validateFiles: false);
            streamer.Init(_serializer);

            // Write raw payload without header
            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            var payload = _serializer.Serialize(expected);
            File.WriteAllBytes(filePath, payload);

            // Act
            var result = streamer.LoadFromFile();

            // Assert
            Assert.AreEqual(expected.TestData, result.TestData);
        }

        #endregion

        #region Saving Tests

        [Test]
        public void SaveToFile_WithNullSavestate_ThrowsArgumentNullException()
        {
            // Arrange
            var streamer = CreateStreamer();
            streamer.Init(_serializer);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => streamer.SaveToFile(null));
        }

        [Test]
        public void SaveToFile_WhenNotDirty_SkipsSave()
        {
            // Arrange
            var savestate = new TestSavestate { Version = 1, TestData = "data", IsDirty = false };
            var streamer = CreateStreamer();
            streamer.Init(_serializer);

            // Act
            streamer.SaveToFile(savestate);

            // Assert
            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            Assert.IsFalse(File.Exists(filePath));
        }

        [Test]
        public void SaveToFile_WhenDirty_WritesSavestate()
        {
            // Arrange
            var savestate = new TestSavestate { Version = 1, TestData = "data", IsDirty = true };
            var streamer = CreateStreamer();
            streamer.Init(_serializer);

            // Act
            streamer.SaveToFile(savestate);

            // Assert
            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            Assert.IsTrue(File.Exists(filePath));
        }

        [Test]
        public void SaveToFile_RemovesDirtyFlag()
        {
            // Arrange
            var savestate = new TestSavestate { Version = 1, TestData = "data", IsDirty = true };
            var streamer = CreateStreamer();
            streamer.Init(_serializer);

            // Act
            streamer.SaveToFile(savestate);

            // Assert
            Assert.IsFalse(savestate.IsDirty);
        }

        [Test]
        public void SaveToFile_WithValidation_WritesHeader()
        {
            // Arrange
            var savestate = new TestSavestate { Version = 1, TestData = "data", IsDirty = true };
            var streamer = CreateStreamer(validateFiles: true);
            streamer.Init(_serializer);

            // Act
            streamer.SaveToFile(savestate);

            // Assert
            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            var fileBytes = File.ReadAllBytes(filePath);

            var magic = System.Text.Encoding.ASCII.GetString(fileBytes, 0, 4);
            Assert.AreEqual("SAVE", magic);
        }

        [Test]
        public void SaveToFile_WithoutValidation_WritesRawPayload()
        {
            // Arrange
            var savestate = new TestSavestate { Version = 1, TestData = "data", IsDirty = true };
            var streamer = CreateStreamer(validateFiles: false);
            streamer.Init(_serializer);

            // Act
            streamer.SaveToFile(savestate);

            // Assert
            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            var fileBytes = File.ReadAllBytes(filePath);

            // Should be able to deserialize directly
            var loaded = _serializer.Deserialize(fileBytes);
            Assert.AreEqual(savestate.TestData, loaded.TestData);
        }

        [Test]
        public void SaveToFile_RoundTrip_PreservesData()
        {
            // Arrange
            var original = new TestSavestate { Version = 1, TestData = "roundtrip_data", IsDirty = true };
            var streamer = CreateStreamer();
            streamer.Init(_serializer);

            // Act - Save then Load
            streamer.SaveToFile(original);
            var loaded = streamer.LoadFromFile();

            // Assert
            Assert.AreEqual(original.TestData, loaded.TestData);
            Assert.AreEqual(original.Version, loaded.Version);
        }

        #endregion

        #region Backup Tests

        [Test]
        public void SaveToFile_CreatesBackup()
        {
            // Arrange
            var first = new TestSavestate { Version = 1, TestData = "first", IsDirty = true };
            var second = new TestSavestate { Version = 1, TestData = "second", IsDirty = true };
            var streamer = CreateStreamer(backupCount: 2);
            streamer.Init(_serializer);

            // Act
            streamer.SaveToFile(first);
            streamer.SaveToFile(second);

            // Assert
            var backup0Path = Path.Combine(_testDirectory, "savestate.backup0.sav");
            Assert.IsTrue(File.Exists(backup0Path));
        }

        [Test]
        public void SaveToFile_RotatesBackups()
        {
            // Arrange
            var streamer = CreateStreamer(backupCount: 3);
            streamer.Init(_serializer);

            // Act - Save multiple times
            for (int i = 1; i <= 4; i++)
            {
                var savestate = new TestSavestate { Version = 1, TestData = $"save_{i}", IsDirty = true };
                streamer.SaveToFile(savestate);
            }

            // Assert - Should have 3 backups
            Assert.IsTrue(File.Exists(Path.Combine(_testDirectory, "savestate.backup0.sav")));
            Assert.IsTrue(File.Exists(Path.Combine(_testDirectory, "savestate.backup1.sav")));
            Assert.IsTrue(File.Exists(Path.Combine(_testDirectory, "savestate.backup2.sav")));
            Assert.IsFalse(File.Exists(Path.Combine(_testDirectory, "savestate.backup3.sav"))); // Oldest deleted
        }

        [Test]
        public void CreateBackup_ManuallyCreatesBackup()
        {
            // Arrange
            var savestate = new TestSavestate { Version = 1, TestData = "data", IsDirty = true };
            var streamer = CreateStreamer(backupCount: 2);
            streamer.Init(_serializer);
            streamer.SaveToFile(savestate);

            // Act
            streamer.CreateBackup();

            // Assert
            var backup0Path = Path.Combine(_testDirectory, "savestate.backup0.sav");
            Assert.IsTrue(File.Exists(backup0Path));
        }

        #endregion

        #region Migration Tests

        [Test]
        public void LoadFromFile_WithMigrator_MigratesOldVersion()
        {
            // Arrange
            var oldSavestate = new TestSavestate { Version = 1, TestData = "v1_data", IsDirty = true };
            var migrator = new TestMigrator(currentVersion: 2);
            var streamer = CreateStreamer();
            streamer.Init(_serializer, migrator);

            // Create old version file
            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            var payload = _serializer.Serialize(oldSavestate);
            var wrapped = WrapWithHeader(payload, oldSavestate.Version);
            File.WriteAllBytes(filePath, wrapped);

            // Act
            var result = streamer.LoadFromFile();

            // Assert
            Assert.AreEqual(2, result.Version);
            Assert.AreEqual("v1_data_migrated", result.TestData);
        }

        [Test]
        public void LoadFromFile_WithCurrentVersion_SkipsMigration()
        {
            // Arrange
            var savestate = new TestSavestate { Version = 2, TestData = "v2_data", IsDirty = true };
            var migrator = new TestMigrator(currentVersion: 2);
            var streamer = CreateStreamer();
            streamer.Init(_serializer, migrator);

            // Create current version file
            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            var payload = _serializer.Serialize(savestate);
            var wrapped = WrapWithHeader(payload, savestate.Version);
            File.WriteAllBytes(filePath, wrapped);

            // Act
            var result = streamer.LoadFromFile();

            // Assert
            Assert.AreEqual(2, result.Version);
            Assert.AreEqual("v2_data", result.TestData); // Not migrated
        }

        [Test]
        public void LoadFromFile_WithFailedMigration_ReturnsFallback()
        {
            // Arrange
            var fallback = new TestSavestate { Version = 2, TestData = "fallback" };
            var oldSavestate = new TestSavestate { Version = 1, TestData = "v1_data", IsDirty = true };
            var migrator = new TestMigrator(currentVersion: 2, shouldFail: true);
            var streamer = CreateStreamer(fallback);
            streamer.Init(_serializer, migrator);

            // Create old version file
            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            var payload = _serializer.Serialize(oldSavestate);
            var wrapped = WrapWithHeader(payload, oldSavestate.Version);
            File.WriteAllBytes(filePath, wrapped);

            // Act
            var result = streamer.LoadFromFile();

            // Assert
            Assert.AreEqual(fallback.TestData, result.TestData);
        }

        #endregion

        #region Utility Tests

        [Test]
        public void DeleteAllSaves_RemovesAllFiles()
        {
            // Arrange
            var streamer = CreateStreamer(backupCount: 2);
            streamer.Init(_serializer);

            // Create multiple saves
            for (int i = 0; i < 3; i++)
            {
                var savestate = new TestSavestate { Version = 1, TestData = $"save_{i}", IsDirty = true };
                streamer.SaveToFile(savestate);
            }

            // Act
            streamer.DeleteAllSaves();

            // Assert
            var files = Directory.GetFiles(_testDirectory);
            Assert.AreEqual(0, files.Length);
        }

        #endregion

        #region File Validation Tests

        [Test]
        public void ValidateFile_WithValidFile_ReturnsTrue()
        {
            // Arrange
            var savestate = new TestSavestate { Version = 1, TestData = "data" };
            var payload = _serializer.Serialize(savestate);
            var fileBytes = WrapWithHeader(payload, savestate.Version);

            // This tests internal validation logic
            // We'll verify by doing a successful load
            var streamer = CreateStreamer();
            streamer.Init(_serializer);
            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            File.WriteAllBytes(filePath, fileBytes);

            // Act
            var result = streamer.LoadFromFile();

            // Assert
            Assert.AreEqual(savestate.TestData, result.TestData);
        }

        [Test]
        public void ValidateFile_WithTooSmallFile_ReturnsFallback()
        {
            // Arrange
            var fallback = new TestSavestate { Version = 1, TestData = "fallback" };
            var streamer = CreateStreamer(fallback);
            streamer.Init(_serializer);

            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            File.WriteAllBytes(filePath, new byte[] { 0x00, 0x01 }); // Too small

            // Act
            var result = streamer.LoadFromFile();

            // Assert
            Assert.AreEqual(fallback.TestData, result.TestData);
        }

        [Test]
        public void ValidateFile_WithInvalidVersion_ReturnsFallback()
        {
            // Arrange
            var fallback = new TestSavestate { Version = 1, TestData = "fallback" };
            var streamer = CreateStreamer(fallback);
            streamer.Init(_serializer);

            // Create file with invalid version (1000)
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("SAVE"));
            writer.Write(1000); // Invalid version
            writer.Write(new byte[10]); // Dummy payload

            var filePath = Path.Combine(_testDirectory, "savestate.sav");
            File.WriteAllBytes(filePath, ms.ToArray());

            // Act
            var result = streamer.LoadFromFile();

            // Assert
            Assert.AreEqual(fallback.TestData, result.TestData);
        }

        #endregion

        #region Helper Methods

        private SavestateFileIOStreamer<TestSavestate> CreateStreamer(
            TestSavestate fallback = null,
            TestSavestate testSavestate = null,
            bool useTestSavestate = false,
            bool debugMode = false,
            int backupCount = 0,
            bool validateFiles = true,
            string customFolderPath = null)
        {
            fallback ??= new TestSavestate { Version = 1, TestData = "default_fallback" };

            return new SavestateFileIOStreamer<TestSavestate>(
                fallback,
                testSavestate,
                useTestSavestate,
                debugMode,
                backupCount,
                validateFiles,
                customFolderPath ?? _testDirectory
            );
        }

        private byte[] WrapWithHeader(byte[] payload, int version)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("SAVE"));
            writer.Write(version);
            writer.Write(payload);
            return ms.ToArray();
        }

        #endregion
    }

    #region Test Support Classes

    [Serializable]
    public class TestSavestate : ISerializableSavestate
    {
        public int Version { get; set; } = 1;
        public bool IsDirty { get; set; }
        public string TestData { get; set; }

        public void RemoveDirtyFlag()
        {
            IsDirty = false;
        }
    }

    public class TestSerializer : ISerializer<TestSavestate>
    {
        public byte[] Serialize(TestSavestate savestate)
        {
            var json = JsonUtility.ToJson(savestate);
            return System.Text.Encoding.UTF8.GetBytes(json);
        }

        public TestSavestate Deserialize(byte[] data)
        {
            var json = System.Text.Encoding.UTF8.GetString(data);
            return JsonUtility.FromJson<TestSavestate>(json);
        }

        public string ConvertToJson(byte[] data)
        {
            return System.Text.Encoding.UTF8.GetString(data);
        }
    }

    public class TestMigrator : ISavestateMigrator<TestSavestate>
    {
        private readonly int _currentVersion;
        private readonly bool _shouldFail;

        public TestMigrator(int currentVersion, bool shouldFail = false)
        {
            _currentVersion = currentVersion;
            _shouldFail = shouldFail;
        }

        public int CurrentVersion => _currentVersion;

        public bool TryMigrate(TestSavestate savestate, out TestSavestate migratedSavestate)
        {
            if (_shouldFail)
            {
                migratedSavestate = null;
                return false;
            }

            migratedSavestate = new TestSavestate
            {
                Version = CurrentVersion,
                TestData = savestate.TestData + "_migrated",
                IsDirty = false
            };

            return true;
        }
    }

    #endregion
}
