using Newtonsoft.Json;

namespace CommonLib.Models;

public class PenumbraModPath
{
    [JsonProperty("ModDirectory")]
    public string ModDirectory { get; set; }
}