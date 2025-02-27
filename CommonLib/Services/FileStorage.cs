using PenumbraModForwarder.Common.Interfaces;

namespace PenumbraModForwarder.Common.Services;

public class FileStorage : IFileStorage
{
    public bool Exists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }

    public string Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The file at path '{path}' does not exist.", path);
        }
        return File.ReadAllText(path);
    }

    public void Write(string path, string content)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
        File.WriteAllText(path, content);
    }

    public void CreateDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public void WriteAllText(string path, string content)
    {
        Write(path, content);
    }

    public void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public Stream CreateFile(string path)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    }

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"The source file at '{sourcePath}' does not exist.", sourcePath);
        }
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!Directory.Exists(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }
        File.Copy(sourcePath, destinationPath, overwrite);
    }

    public Stream OpenRead(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The file at '{path}' does not exist.", path);
        }
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public Stream OpenWrite(string path)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
    }
}