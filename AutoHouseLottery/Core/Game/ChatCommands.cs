using ECommons.Automation;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Game;

internal static class ChatCommands
{
    public static void Dispatch(string commandBlock)
    {
        if (string.IsNullOrWhiteSpace(commandBlock))
        {
            return;
        }

        var lines = commandBlock.Split('\n');
        var delayMs = 0;
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var command = lines[lineIndex].Trim();
            if (command.Length == 0)
            {
                continue;
            }

            if (!command.StartsWith('/'))
            {
                RunLog.Warning($"Post-run line skipped, commands must start with '/': {command}");
                continue;
            }

            Svc.Framework.RunOnTick(() => Send(command), TimeSpan.FromMilliseconds(delayMs));
            delayMs += HouseLotteryConstants.PostRunCommandSpacingMs;
        }
    }

    private static void Send(string command)
    {
        try
        {
            RunLog.Info($"Post-run command: {command}");
            Chat.ExecuteCommand(command);
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, $"Post-run command '{command}' failed");
        }
    }
}
