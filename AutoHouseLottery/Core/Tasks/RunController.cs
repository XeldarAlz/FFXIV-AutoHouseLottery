namespace AutoHouseLottery.Core.Tasks;

// The single owner of game-changing work, so entries, result checks, refunds and claims never drive the game at the
// same time. Runs arrive with the entry and settlement drivers; until then nothing holds it and every request is a no-op.
internal sealed class RunController
{
    public PauseReason PauseReason { get; private set; } = PauseReason.None;

    public bool Paused => PauseReason != PauseReason.None;

    public bool Running { get; private set; }

    public void Pause(PauseReason reason)
    {
        if (!Running || Paused)
        {
            return;
        }

        PauseReason = reason;
        RunLog.Info($"Run paused ({reason}).");
    }

    public void Resume()
    {
        if (!Paused)
        {
            return;
        }

        PauseReason = PauseReason.None;
        RunLog.Info("Run resumed.");
    }

    public void TogglePause()
    {
        if (Paused)
        {
            Resume();
            return;
        }

        Pause(PauseReason.Manual);
    }

    public void Stop() => End("Run stopped by the player.");

    public void StopForAlert() => End("Run stopped by the GM alert.");

    public void Interrupt() => End("Run interrupted by the logout.");

    public void Shutdown() => End("Run ended because the plugin is unloading.");

    private void End(string reason)
    {
        if (!Running)
        {
            return;
        }

        Running = false;
        PauseReason = PauseReason.None;
        RunLog.Info(reason);
    }
}
