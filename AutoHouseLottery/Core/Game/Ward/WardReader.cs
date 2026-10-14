using AutoHouseLottery.Core.Game.Model;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;

namespace AutoHouseLottery.Core.Game.Ward;

// Reads the ward list as the window shows it, with no request to tie it to; WardPager adds that correlation.
internal static unsafe class WardReader
{
    public const string AddonName = "HousingSelectBlock";
    public const int NoWard = -1;

    // Only the ward number and the row count, so a poll can rule a request out before reading sixty rows.
    public static WardWindowHeader ReadHeader(long lastUpdateSequence)
    {
        if (!AddonReads.TryGetReady(AddonName, out var addon))
        {
            return new WardWindowHeader(false, false, NoWard, lastUpdateSequence);
        }

        var valid = HeaderFits(addon, out var shownWardIndex);
        return new WardWindowHeader(true, valid, shownWardIndex, lastUpdateSequence);
    }

    public static WardReadStatus ReadDisplayed(long updateSequence, out WardReading? reading)
    {
        reading = null;
        if (!AddonReads.TryGetReady(AddonName, out var addon))
        {
            return WardReadStatus.WindowClosed;
        }

        if (!HeaderFits(addon, out var shownWardIndex))
        {
            return WardReadStatus.LayoutMismatch;
        }

        var values = addon->AtkValues;
        var rows = new WardPlotRow[WardLayout.MaxRows];
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var rowStatus = ReadRow(values, rowIndex, out rows[rowIndex]);
            if (rowStatus != WardReadStatus.Read)
            {
                return rowStatus;
            }
        }

        reading = new WardReading(
            shownWardIndex,
            AddonReads.Text(values[WardLayout.WardNameValue]),
            WardCount(values[WardLayout.WardCountValue]),
            AddonReads.Unsigned(values[WardLayout.BuyerFlagsValue]),
            PortalTerritory(),
            Svc.PlayerState.CurrentWorld.RowId,
            ImmutableCollectionsMarshal.AsImmutableArray(rows),
            null,
            updateSequence,
            WardCorrelation.Unrequested,
            DateTime.UtcNow);
        return WardReadStatus.Read;
    }

    // The territory of the district whose wards the portal agent is listing; null when the agent is not there.
    public static uint? PortalTerritory()
    {
        var agent = AgentHousingPortal.Instance();
        return agent == null ? null : agent->TerritoryType;
    }

    private static bool HeaderFits(AtkUnitBase* addon, out int shownWardIndex)
    {
        shownWardIndex = NoWard;
        var values = addon->AtkValues;
        var valueCount = (int)addon->AtkValuesCount;
        if (values == null || valueCount <= WardLayout.FirstRowValue
            || !AddonReads.IsInteger(values[WardLayout.WardIndexValue])
            || !AddonReads.IsInteger(values[WardLayout.RowCountValue]))
        {
            return false;
        }

        shownWardIndex = (int)AddonReads.Unsigned(values[WardLayout.WardIndexValue]);
        return WardLayout.RowsFit((int)AddonReads.Unsigned(values[WardLayout.RowCountValue]), valueCount);
    }

    private static WardReadStatus ReadRow(AtkValue* values, int rowIndex, out WardPlotRow row)
    {
        row = default;
        ref readonly var status = ref values[WardLayout.RowValue(rowIndex, WardLayout.StatusOffset)];
        ref readonly var size = ref values[WardLayout.RowValue(rowIndex, WardLayout.SizeTextOffset)];
        ref readonly var nameOrPrice = ref values[WardLayout.RowValue(rowIndex, WardLayout.NameOrPriceOffset)];
        if (status.Type == AtkValueType.Undefined || size.Type == AtkValueType.Undefined || nameOrPrice.Type == AtkValueType.Undefined)
        {
            return WardReadStatus.Incomplete;
        }

        if (!AddonReads.IsInteger(status) || !AddonReads.IsText(size) || !AddonReads.IsText(nameOrPrice))
        {
            return WardReadStatus.LayoutMismatch;
        }

        row = new WardPlotRow(
            rowIndex,
            AddonReads.Unsigned(status),
            AddonReads.Text(size),
            AddonReads.Text(nameOrPrice),
            AddonReads.Unsigned(values[WardLayout.RowValue(rowIndex, WardLayout.UnidentifiedOffset)]),
            AddonReads.Unsigned(values[WardLayout.RowValue(rowIndex, WardLayout.FirstTagOffset)]),
            AddonReads.Unsigned(values[WardLayout.RowValue(rowIndex, WardLayout.FirstTagOffset + 1)]),
            AddonReads.Unsigned(values[WardLayout.RowValue(rowIndex, WardLayout.FirstTagOffset + 2)]));
        return WardReadStatus.Read;
    }

    private static int? WardCount(in AtkValue value)
    {
        if (!AddonReads.IsInteger(value))
        {
            return null;
        }

        var count = (int)AddonReads.Unsigned(value);
        return count is > 0 and <= ResidentialDistricts.WardCount ? count : null;
    }
}
