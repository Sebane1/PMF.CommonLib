using AutoMapper;
using PenumbraModForwarder.Common.Models;

namespace PenumbraModForwarder.Common.Services;

/// <summary>
/// A class for converting old configuration models into the new configuration models using AutoMapper.
/// </summary>
public class ConvertConfiguration : Profile
{
    public ConvertConfiguration()
    {
        CreateMap<OldConfigModel.OldConfigurationModel, ConfigurationModel>()
            // AutoDelete -> BackgroundWorker.AutoDelete
            .ForMember(
                dest => dest.BackgroundWorker.AutoDelete,
                opt => opt.MapFrom(src => src.AutoDelete))

            // FileLinkingEnabled -> Common.FileLinkingEnabled
            .ForMember(
                dest => dest.Common.FileLinkingEnabled,
                opt => opt.MapFrom(src => src.FileLinkingEnabled))

            // StartOnBoot -> Common.StartOnBoot
            .ForMember(
                dest => dest.Common.StartOnBoot,
                opt => opt.MapFrom(src => src.StartOnBoot))

            // Convert DownloadPath (string) to a List<string> for BackgroundWorker.DownloadPath
            .ForMember(
                dest => dest.BackgroundWorker.DownloadPath,
                opt => opt.MapFrom(src =>
                    string.IsNullOrWhiteSpace(src.DownloadPath)
                        ? new List<string>()
                        : new List<string> { src.DownloadPath }))

            // TexToolPath -> BackgroundWorker.TexToolPath
            .ForMember(
                dest => dest.BackgroundWorker.TexToolPath,
                opt => opt.MapFrom(src => src.TexToolPath))

            // AdvancedOptions.PenumbraTimeOutInSeconds -> AdvancedConfigurationModel.PenumbraTimeOutInSeconds
            .ForPath(
                dest => dest.AdvancedOptions.PenumbraTimeOutInSeconds,
                opt => opt.MapFrom(src => src.AdvancedOptions.PenumbraTimeOutInSeconds))
            .ReverseMap();

        // Map old advanced config to new advanced config (optional custom mappings)
        CreateMap<OldConfigModel.OldAdvancedConfigurationModel, AdvancedConfigurationModel>()
            // Example: map HideWindowOnStartup -> MinimiseToTray if desired:
            // .ForMember(
            //     dest => dest.MinimiseToTray,
            //     opt => opt.MapFrom(src => src.HideWindowOnStartup))
            .ReverseMap();
    }
}