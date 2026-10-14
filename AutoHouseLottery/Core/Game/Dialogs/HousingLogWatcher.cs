using AutoHouseLottery.Core.Game.Model;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Game.Dialogs;

// Watches the game's log messages for the housing rejections by their row ID, so no text is parsed. Keeps the last few
// for the probe and the entry run's rejection check. While a run awaits an entry's receipt it also records every log
// message row and system line it sees, since the row of the receipt line itself is not known yet.
internal sealed class HousingLogWatcher : IDisposable
{
    private const int Capacity = 8;
    private const int FirstParameter = 0;

    private readonly HousingLogObservation[] recent = new HousingLogObservation[Capacity];
    private readonly object gate = new();
    private int next;
    private int count;
    private volatile bool capturing;

    public HousingLogWatcher()
    {
        Svc.Chat.LogMessage += OnLogMessage;
        Svc.Chat.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        Svc.Chat.LogMessage -= OnLogMessage;
        Svc.Chat.ChatMessage -= OnChatMessage;
    }

    public void BeginCapture()
    {
        capturing = true;
        RunLog.Info("Housing log: recording every log message row and system line until the entry's receipt is settled.");
    }

    public void EndCapture() => capturing = false;

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

    // The earliest refusal of a lottery entry the game logged after the given sequence.
    public HousingLogObservation? FirstRejectionAfter(long sequence)
    {
        Span<HousingLogObservation> seen = stackalloc HousingLogObservation[Capacity];
        var copied = CopyRecent(seen);
        HousingLogObservation? earliest = null;
        for (var index = 0; index < copied; index++)
        {
            var observation = seen[index];
            if (observation.Sequence > sequence && HousingTextRows.EntryRejections.Contains(observation.Text))
            {
                earliest = observation;
            }
        }

        return earliest;
    }

    private void OnLogMessage(ILogMessage message)
    {
        int? firstNumber = message.TryGetIntParameter(FirstParameter, out var number) ? number : null;
        if (!HousingTextRows.TryFindLogMessage(message.LogMessageId, out var text))
        {
            if (capturing)
            {
                RunLog.Info($"Housing log capture: LogMessage {message.LogMessageId}{(firstNumber is { } shown ? $", first number {shown}" : string.Empty)}.");
            }

            return;
        }

        var observation = new HousingLogObservation(text, message.LogMessageId, firstNumber, DateTime.UtcNow, GameSequence.Next());
        lock (gate)
        {
            recent[next] = observation;
            next = (next + 1) % Capacity;
            count = Math.Min(count + 1, Capacity);
        }

        RunLog.Info($"Housing log message: LogMessage {message.LogMessageId} ({text}){(firstNumber is { } value ? $", first number {value}" : string.Empty)}.");
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!capturing || message.LogKind is not (XivChatType.SystemMessage or XivChatType.ErrorMessage or XivChatType.GatheringSystemMessage))
        {
            return;
        }

        RunLog.Info($"Housing log capture: {message.LogKind} line '{message.Message.TextValue}'.");
    }
}
