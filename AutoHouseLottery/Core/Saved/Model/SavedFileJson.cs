namespace AutoHouseLottery.Core.Saved.Model;

internal sealed class SavedFileJson
{
    public int? Schema { get; init; }

    public SavedSearchJson[]? Searches { get; init; }

    public SavedListJson[]? Lists { get; init; }

    public DeliveredNoticeJson[]? Delivered { get; init; }

    public NoticeRecordJson[]? Recent { get; init; }
}
