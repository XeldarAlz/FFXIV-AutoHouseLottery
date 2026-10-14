namespace AutoHouseLottery.Core.Providers.Model;

// A live response may move the region's lottery calendar; a response replayed from the disk cache only reads it.
internal enum CalendarUse : byte { Anchor, ReadOnly }
