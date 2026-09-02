namespace ExcalidrawDesktop.Core;

public sealed record SessionMoveState(
    bool IsMoving,
    bool IsInitializing,
    bool IsSaving,
    bool IsClosing,
    bool HasOpenDialog,
    bool IsSuspensionChanging,
    bool IsResuming,
    bool IsUnloading);

public static class SessionMoveRules
{
    public static bool CanMove(SessionMoveState state) =>
        !state.IsMoving &&
        !state.IsInitializing &&
        !state.IsSaving &&
        !state.IsClosing &&
        !state.HasOpenDialog &&
        !state.IsSuspensionChanging &&
        !state.IsResuming &&
        !state.IsUnloading;
}
