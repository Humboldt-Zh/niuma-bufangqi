using System;
using System.Runtime.InteropServices;

namespace NiumaBufangqi.Desktop.Taskbar;

/// <summary>Signals Shell stacking changes promptly; the placement timer remains a fallback.</summary>
internal sealed class EntryLayerMonitor : IDisposable
{
    private const uint Foreground = 0x0003;
    private const uint Reorder = 0x8004;
    private const uint SkipOwnProcess = 0x0002;
    private readonly Action changed;
    private readonly WinEventProcedure callback;
    private IntPtr foregroundHook;
    private IntPtr reorderHook;
    private IntPtr taskbar;
    private bool disposed;

    internal EntryLayerMonitor(Action changed)
    {
        this.changed = changed;
        callback = OnWindowEvent;
        // OUTOFCONTEXT (zero) delivers callbacks on the registering UI thread.
        // Do not inject a hook DLL or react to our own positioning notifications.
        foregroundHook = SetWinEventHook(Foreground, Foreground, IntPtr.Zero, callback, 0, 0, SkipOwnProcess);
    }

    internal void ObserveTaskbar(IntPtr handle)
    {
        if (disposed) return;
        if (foregroundHook == IntPtr.Zero)
            foregroundHook = SetWinEventHook(Foreground, Foreground, IntPtr.Zero, callback, 0, 0, SkipOwnProcess);
        if (taskbar == handle && (handle == IntPtr.Zero || reorderHook != IntPtr.Zero)) return;
        if (reorderHook != IntPtr.Zero) UnhookWinEvent(reorderHook);
        reorderHook = IntPtr.Zero;
        taskbar = handle;
        if (handle == IntPtr.Zero || GetWindowThreadProcessId(handle, out var process) == 0) return;
        // Restrict noisy accessibility reorder events to Explorer's taskbar
        // process, then filter unrelated Explorer windows in the callback.
        reorderHook = SetWinEventHook(Reorder, Reorder, IntPtr.Zero, callback, process, 0, SkipOwnProcess);
    }

    private void OnWindowEvent(IntPtr hook, uint eventId, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (disposed) return;
        if (eventId == Foreground || eventId == Reorder && window != IntPtr.Zero && taskbar != IntPtr.Zero
            && (window == taskbar || GetAncestor(window, 2) == taskbar)) changed();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
        if (reorderHook != IntPtr.Zero) UnhookWinEvent(reorderHook);
        foregroundHook = reorderHook = taskbar = IntPtr.Zero;
    }

    private delegate void WinEventProcedure(IntPtr hook, uint eventId, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventProcedure callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);
}
