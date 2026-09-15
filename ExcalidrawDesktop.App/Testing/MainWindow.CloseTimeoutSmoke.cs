using ExcalidrawDesktop.App.Sessions;
#if DEBUG
using ExcalidrawDesktop.App.Models;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
    private async Task VerifyCloseTimeoutSmokeAsync(DocumentSession session)
    {
        var timeout = windowClose.SaveResponseTimeoutForSmoke;
        try
        {
            windowClose.SuppressSaveRequestForSmoke = true;
            windowClose.SaveResponseTimeoutForSmoke = TimeSpan.FromMilliseconds(200);
            session.ForceDirtyForSmoke = true;
            session.IsDirty = true;
            var windowWait = windowClose.RequestSaveForWindowCloseAsync(session);
            var expiredId = session.CloseRequestId ?? throw new InvalidOperationException("Missing close ID.");
            if (await windowWait || session.WindowCloseSaveCompletion is not null ||
                session.CloseRequestId is not null || !sessions.Contains(session) || !session.IsDirty)
            {
                throw new InvalidOperationException("A silent editor did not cancel window closing safely.");
            }
            if (await windowClose.RequestSaveForWindowCloseAsync(session) || session.ClosePromptOpen ||
                session.CloseCompletion is not null || !sessions.Contains(session))
            {
                throw new InvalidOperationException("A silent editor did not release explicit save controls.");
            }

            windowClose.SaveResponseTimeoutForSmoke = TimeSpan.FromSeconds(5);
            var retry = windowClose.RequestSaveForWindowCloseAsync(session);
            var currentId = session.CloseRequestId ?? throw new InvalidOperationException("Missing retry ID.");
            session.ForceDirtyForSmoke = false;
            session.IsDirty = false;
            windowClose.OnCloseReady(session, expiredId);
            windowClose.OnCloseCancelled(session, expiredId);
            if (session.WindowCloseSaveCompletion?.Task.IsCompleted != false || session.CloseRequestId != currentId)
            {
                throw new InvalidOperationException("A late close acknowledgement affected a newer request.");
            }
            windowClose.OnCloseReady(session, currentId);
            if (!await retry || session.CloseRequestId is not null || session.WindowCloseSaveCompletion is not null)
            {
                throw new InvalidOperationException("Closing could not be retried after a timeout.");
            }
        }
        finally
        {
            windowClose.OnCloseCancelled(session);
            windowClose.SuppressSaveRequestForSmoke = false;
            windowClose.SaveResponseTimeoutForSmoke = timeout;
            session.ForceDirtyForSmoke = false;
            session.IsDirty = false;
            UpdateTabHeader(session);
        }
    }
}
#endif
