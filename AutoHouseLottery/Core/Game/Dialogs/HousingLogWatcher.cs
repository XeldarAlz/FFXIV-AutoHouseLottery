using AutoHouseLottery.Core.Game.Model;
using Dalamud.Game.Chat;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Game.Dialogs;

// Watches the game's log messages for the housing rejections (LogMessage 3420, 3497, 6099, 6100) by their row ID, so no
// text is parsed. Keeps the last few for the probe and later for the entry driver's rejection check.
internal sealed class HousingLogWatcher : IDisposable
{
    private const int Capacity = 8;
    private const int FirstParameter = 0;

    private readonly HousingLogObservation[] recent = new HousingLogObservation[Capacity];
    private readonly object gate = new();
    private int next;
    private int count;

    public HousingLogWatcher()
    {
        Svc.Chat.LogMessage += OnLogMessage;
    }

    public void Dispose() => Svc.Chat.LogMessage -= OnLogMessage;

    // Newest first.
    public int CopyRecent(Span<HousingLogObservation> destination)
    {
        lock (gate)
        {
            var copied = Math.Min(count, destination.Length);
            for (var copyIndex = 0; copyIndex < copied; copyIndex++)
            {
                destination[copyIndex] = recent[(next - 1 - copyIndex + Capacity) % Capacity];
            }

            return copied;
        }
    }

    private void OnLogMessage(ILogMessage message)
    {
        if (!HousingTextRows.TryFindLogMessage(message.LogMessageId, out var text))
        {
            return;
        }

        int? firstNumber = message.TryGetIntParameter(FirstParameter, out var number) ? number : null;
        var observation = new HousingLogObservation(text, message.LogMessageId, firstNumber, DateTime.UtcNow, GameSequence.Next());
        lock (gate)
        {
            recent[next] = observation;
            next = (next + 1) % Capacity;
            count = Math.Min(count + 1, Capacity);
        }

        RunLog.Info($"Housing log message: LogMessage {message.LogMessageId} ({text}){(firstNumber is { } value ? $", first number {value}" : string.Empty)}.");
    }
}
