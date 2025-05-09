using CommonLib.Enums;

namespace CommonLib.Interfaces;

public interface ISoundManagerService
{
    Task PlaySoundAsync(SoundType soundType, float volume = 1.0f);
}