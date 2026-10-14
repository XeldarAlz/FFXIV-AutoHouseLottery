using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using System.Numerics;

namespace AutoHouseLottery.Core.Travel;

// Opens the placard the approach walked up to. The placard is found again by where the approach saw it, never by
// "nearest placard", and opening it only shows the plot's details; which plot it is comes from the placard reading.
internal static unsafe class PlacardInteraction
{
    private const float SamePlacardMeters = 1f;
    private const bool CheckLineOfSight = false;

    public static IGameObject? Find(Vector3 seenAt)
    {
        var objects = Svc.Objects;
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            var candidate = objects[objectIndex];
            if (candidate is { ObjectKind: ObjectKind.EventObj, BaseId: PlacardApproach.PlacardBaseId, IsTargetable: true }
                && Vector3.Distance(candidate.Position, seenAt) <= SamePlacardMeters)
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool InRange(IGameObject placard)
        => Svc.Objects.LocalPlayer is { } player && Vector3.Distance(player.Position, placard.Position) <= PlacardApproach.InteractionRangeMeters;

    public static void Open(IGameObject placard)
    {
        Svc.Targets.Target = placard;
        TargetSystem.Instance()->InteractWithObject((GameObjectStruct*)placard.Address, CheckLineOfSight);
    }
}
