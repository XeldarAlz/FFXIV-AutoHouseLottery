namespace AutoHouseLottery.Core.Find.Model;

[Flags]
internal enum SizeFilter : byte
{
    None = 0,
    Small = 1 << 0,
    Medium = 1 << 1,
    Large = 1 << 2,
    All = Small | Medium | Large,
}
