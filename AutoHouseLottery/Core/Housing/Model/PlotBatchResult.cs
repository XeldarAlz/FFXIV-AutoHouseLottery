namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct PlotBatchResult(MergeRejection BatchRejection, int Merged, int Rejected, int MarkedNotReported)
{
    public bool Accepted => BatchRejection == MergeRejection.None;
}
