using CommonLib.Interfaces;
using CommonLib.Models;
using Newtonsoft.Json;
using NLog;
using SevenZipExtractor;

namespace CommonLib.Services;

public class PenumbraService : IPenumbraService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IConfigurationService _configurationService;
    private readonly IFileStorage _fileStorage;

    private static readonly string[] PenumbraJsonLocations =
    {
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher",
            "pluginConfigs",
            "Penumbra.json"
        ),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncherCN",
            "pluginConfigs",
            "Penumbra.json"
        )
    };

    public PenumbraService(
        IConfigurationService configurationService,
        IFileStorage fileStorage)
    {
        _configurationService = configurationService;
        _fileStorage = fileStorage;
    }
    
    public void InitializePenumbraPath()
    {
        var existingPath = _configurationService.ReturnConfigValue(
            c => c.BackgroundWorker.PenumbraModFolderPath
        ) as string;

        if (!string.IsNullOrWhiteSpace(existingPath))
        {
            _logger.Info("Penumbra path is already set in the configuration.");
            return;
        }

        var foundPath = FindPenumbraPath();
        if (!string.IsNullOrWhiteSpace(foundPath))
        {
            UpdatePenumbraPathInConfiguration(foundPath);
        }
        else
        {
            _logger.Warn("Penumbra path could not be located with any known configuration.");
        }
    }
    
    public string InstallMod(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
            throw new ArgumentNullException(nameof(sourceFilePath));

        var penumbraPath = _configurationService.ReturnConfigValue(
            c => c.BackgroundWorker.PenumbraModFolderPath
        ) as string;

        if (string.IsNullOrEmpty(penumbraPath))
            throw new InvalidOperationException("Penumbra path not configured. Make sure to set it first.");

        if (!_fileStorage.Exists(penumbraPath))
        {
            _fileStorage.CreateDirectory(penumbraPath);
        }

        // Process meta.json to determine destination folder name.
        var destinationFolderName = Path.GetFileNameWithoutExtension(sourceFilePath);
        using (var archiveForMeta = new ArchiveFile(sourceFilePath))
        {
            var metaEntry = archiveForMeta.Entries.FirstOrDefault(
                e => e?.FileName?.Equals("meta.json", StringComparison.OrdinalIgnoreCase) == true
            );

            if (metaEntry != null)
            {
                // Extract only meta.json to a temporary file.
                var tempMetaFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                archiveForMeta.Extract(entry =>
                {
                    if (ReferenceEquals(entry, metaEntry))
                        return tempMetaFilePath;
                    return null;
                });

                try
                {
                    var metaContent = _fileStorage.Read(tempMetaFilePath);
                    var meta = JsonConvert.DeserializeObject<PmpMeta>(metaContent);

                    if (!string.IsNullOrWhiteSpace(meta?.Name))
                    {
                        destinationFolderName = meta.Name;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to parse meta.json in {SourceFile}; using default folder name.", sourceFilePath);
                }
                finally
                {
                    if (_fileStorage.Exists(tempMetaFilePath))
                    {
                        _fileStorage.Delete(tempMetaFilePath);
                    }
                }
            }
            else
            {
                _logger.Warn("No meta.json found in {SourceFile}; using default folder name.", sourceFilePath);
            }
        }

        // Clean up invalid characters for the file system.
        destinationFolderName = RemoveInvalidPathChars(destinationFolderName);

        // Create final destination folder.
        var destinationFolderPath = Path.Combine(penumbraPath, destinationFolderName);
        if (!_fileStorage.Exists(destinationFolderPath))
        {
            _fileStorage.CreateDirectory(destinationFolderPath);
        }
        
        var extractionSuccessful = false;
        const int maxAttempts = 6;
        var attempt = 0;

        while (attempt < maxAttempts && !extractionSuccessful)
        {
            attempt++;
            // If this is a retry, clear the destination folder before reattempting.
            if (attempt > 1)
            {
                _logger.Info("Retrying extraction attempt {Attempt} for {SourceFile}", attempt, sourceFilePath);
                ClearDirectory(destinationFolderPath);
            }
            
            using (var archive = new ArchiveFile(sourceFilePath))
            {
                archive.Extract(entry =>
                {
                    if (entry == null)
                        return null;
                    return Path.Combine(destinationFolderPath, entry.FileName ?? string.Empty);
                });
                
                _logger.Info("Checking for and cleaning up .bak files...");
                if (Directory.Exists(destinationFolderPath))
                {
                    // This is a hack fix for a really eccentric bug someone was running into, some mods were having .json files turned into .json.bak 
                    // Let's just change them
                    var bakFiles = Directory.GetFiles(destinationFolderPath, "*.bak", SearchOption.AllDirectories);
                    foreach (var bakFile in bakFiles)
                    {
                        var originalFileName = bakFile.Substring(0, bakFile.Length - 4);
                        
                        try
                        {
                            if (!File.Exists(originalFileName))
                            {
                                File.Move(bakFile, originalFileName);
                                _logger.Info("Renamed .bak file: {BakFile} -> {OriginalFile}", 
                                    Path.GetFileName(bakFile), Path.GetFileName(originalFileName));
                            }
                            else
                            {
                                File.Delete(bakFile);
                                _logger.Info("Deleted redundant .bak file: {BakFile} (original exists)", 
                                    Path.GetFileName(bakFile));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to process .bak file: {BakFile}", bakFile);
                        }
                    }
                }

                extractionSuccessful = true;
            }

            // Validate that the extracted files match exactly the archive contents.
            using (var archive = new ArchiveFile(sourceFilePath))
            {
                var expectedFiles = archive.Entries
                    .Where(e => e != null && !string.IsNullOrEmpty(e.FileName))
                    .Select(e => Path.Combine(destinationFolderPath, e.FileName))
                    .ToList();

                // Check that each expected file exists.
                var allFilesExtracted = expectedFiles.All(file => _fileStorage.Exists(file));

                if (allFilesExtracted && expectedFiles.Count > 0)
                {
                    extractionSuccessful = true;
                    break;
                }
                else
                {
                    _logger.Warn("Extraction attempt {Attempt} did not extract all expected files for {SourceFile}", attempt, sourceFilePath);
                }
            }
        }

        if (!extractionSuccessful)
        {
            throw new InvalidOperationException($"Failed to fully extract mod files from archive {sourceFilePath} after {maxAttempts} attempts.");
        }

        // Optionally remove the original archive
        // _fileStorage.Delete(sourceFilePath);

        _logger.Info("Installed archive from {Source} into {Destination}", sourceFilePath, destinationFolderPath);
        return destinationFolderPath;
    }

    private string FindPenumbraPath()
    {
        foreach (var location in PenumbraJsonLocations)
        {
            if (_fileStorage.Exists(location))
            {
                var path = ExtractPathFromJson(location);
                if (!string.IsNullOrWhiteSpace(path))
                    return path;
            }
        }

        return string.Empty;
    }

    private string ExtractPathFromJson(string jsonFilePath)
    {
        var fileContent = _fileStorage.Read(jsonFilePath);
        var penumbraData = JsonConvert.DeserializeObject<PenumbraModPath>(fileContent);
        return penumbraData?.ModDirectory ?? string.Empty;
    }

    private void UpdatePenumbraPathInConfiguration(string foundPath)
    {
        _logger.Info("Setting Penumbra path to {FoundPath}", foundPath);

        _configurationService.UpdateConfigValue(
            config => config.BackgroundWorker.PenumbraModFolderPath = foundPath,
            "BackgroundWorker.PenumbraModFolderPath",
            foundPath
        );
    }

    private static string RemoveInvalidPathChars(string text)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(text.Where(ch => !invalidChars.Contains(ch)).ToArray());
    }
    
    private void ClearDirectory(string directoryPath)
    {
        try
        {
            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (_fileStorage.Exists(file))
                {
                    _fileStorage.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to clear directory {DirectoryPath}", directoryPath);
        }
    }
}