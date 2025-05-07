namespace CommonLib.Interfaces;

public interface IFileSystemHelper
{
    bool FileExists(string path);
    IEnumerable<string> GetStandardTexToolsPaths();
}