namespace Kotor.Core.Gff;

/// <summary>CExoLocString : reference TLK (0xFFFFFFFF = aucune) et chaines locales indexees par langId*2+genre.</summary>
public sealed record GffLocString(uint StrRef, IReadOnlyDictionary<int, string> Strings);
