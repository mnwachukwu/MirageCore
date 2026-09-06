// Client entry point. The Velopack update hooks run FIRST, before any window exists, because an
// install or update step may need to exit the process outright.
using Mirage.Client.Core.Diagnostics;
using Mirage.Client.Shell;
using Mirage.Shared;
using Mirage.Updates;
using Velopack;

VelopackApp.Build().Run();

// Look for a newer build in the background and stage it for the next launch. Fire-and-forget on
// purpose: nobody should wait on GitHub to start a game, and AppUpdates swallows every failure, so
// there is no result worth awaiting. Does nothing on macOS or a portable copy.
_ = AppUpdates.StageForNextLaunchAsync(UpdatableApp.Client);

// Opened before the window, so a failure during content load or graphics init is recorded rather than
// lost — those are the crashes with nothing on screen to describe them.
ClientLog.Initialize(AppPaths.Cache("logs"));
ClientLog.InstallCrashHandler();

try
{
    using var game = new MirageGame();
    game.Run();
    ClientLog.Shutdown("closed normally");
}
catch (Exception ex)
{
    ClientLog.Fatal(ex, "Unhandled exception out of the game loop.");
    ClientLog.Shutdown("crashed");
    throw;
}
