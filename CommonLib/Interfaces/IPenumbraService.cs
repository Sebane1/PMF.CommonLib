namespace CommonLib.Interfaces;

public interface IPenumbraService
{
    void InitializePenumbraPath();
    string InstallMod(string sourceFilePath);
}