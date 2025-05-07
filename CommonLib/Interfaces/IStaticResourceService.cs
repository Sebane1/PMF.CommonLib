using CommonLib.Models;

namespace CommonLib.Interfaces;

public interface IStaticResourceService
{
    Task<(GithubStaticResources.InformationJson?, GithubStaticResources.UpdaterInformationJson?)>
        GetResourcesUsingGithubApiAsync();
}