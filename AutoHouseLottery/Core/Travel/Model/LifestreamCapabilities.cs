namespace AutoHouseLottery.Core.Travel.Model;

internal readonly record struct LifestreamCapabilities(
    bool Installed,
    bool GoToHousingAddress,
    bool IsBusy,
    bool Abort,
    bool IsHere,
    bool IsQuickTravelAvailable,
    bool GetPlotEntrance,
    bool GetCurrentPlotInfo)
{
    public bool ChangeCharacter { get; init; }

    // Travel needs the request and the busy flag: without the flag the plugin could not tell its work from anyone else's.
    public bool CanTravel => Installed && GoToHousingAddress && IsBusy;

    // A character switch needs the same: the request, and the busy flag to tell its own login apart from anything else.
    public bool CanSwitchCharacters => Installed && ChangeCharacter && IsBusy;
}

internal enum TravelStartResult : byte
{
    Started,
    InvalidDestination,
    NotInstalled,
    DependencyBusy,
    AlreadyTravelling,
    RequestFailed,
}

internal readonly record struct TravelStart(TravelStartResult Result, long Token);
