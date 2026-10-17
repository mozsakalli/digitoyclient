// core.upm Extensions/AwaitableExtensions.cs adaptasyonu: Awaitable → Task; Debug.LogException → Log.Error.
using System;
using System.Threading.Tasks;
using DigitoyEngine;

public static class TaskExtensions
{
    /// <summary>Ateşle-unut. İstisnaları loglar; iptal normal akış sayılır.</summary>
    public static async void Forget(this Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.LogError(ex); }
    }

    public static async void Forget<T>(this Task<T> task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.LogError(ex); }
    }
}
