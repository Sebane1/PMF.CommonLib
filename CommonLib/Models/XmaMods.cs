using CommonLib.Enums;
using MessagePack;

namespace CommonLib.Models;

[MessagePackObject]
public class XmaMods
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;

    [Key(1)]
    public string Publisher { get; set; } = string.Empty;

    [Key(2)]
    public string Type { get; set; } = string.Empty;

    [Key(3)]
    public string ImageUrl { get; set; } = string.Empty;

    [Key(4)]
    public string ModUrl { get; set; } = string.Empty;
    
    [Key(5)]
    public XmaGender Gender { get; set; } = XmaGender.Unisex;
}