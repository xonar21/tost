#region сборка netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51
// netstandard.dll
#endregion

using System.Runtime.CompilerServices;

namespace System.Threading.Tasks
{
    public class Task<TResult> : Task
    {
        public Task(Func<TResult> function);
        public Task(Func<TResult> function, CancellationToken cancellationToken);
        public Task(Func<TResult> function, TaskCreationOptions creationOptions);
        public Task(Func<object, TResult> function, object state);
        public Task(Func<TResult> function, CancellationToken cancellationToken, TaskCreationOptions creationOptions);
        public Task(Func<object, TResult> function, object state, CancellationToken cancellationToken);
        public Task(Func<object, TResult> function, object state, TaskCreationOptions creationOptions);
        public Task(Func<object, TResult> function, object state, CancellationToken cancellationToken, TaskCreationOptions creationOptions);

        public static TaskFactory<TResult> Factory { get; }
        public TResult Result { get; }

        public ConfiguredTaskAwaitable<TResult> ConfigureAwait(bool continueOnCapturedContext);
        public Task ContinueWith(Action<Task<TResult>, object> continuationAction, object state, CancellationToken cancellationToken);
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, object, TNewResult> continuationFunction, object state, TaskScheduler scheduler);
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, object, TNewResult> continuationFunction, object state, TaskContinuationOptions continuationOptions);
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, object, TNewResult> continuationFunction, object state, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler);
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, object, TNewResult> continuationFunction, object state, CancellationToken cancellationToken);
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, object, TNewResult> continuationFunction, object state);
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult> continuationFunction, TaskScheduler scheduler);
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult> continuationFunction, TaskContinuationOptions continuationOptions);
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult> continuationFunction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler);
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult> continuationFunction, CancellationToken cancellationToken);
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult> continuationFunction);
        public Task ContinueWith(Action<Task<TResult>> continuationAction, TaskScheduler scheduler);
        public Task ContinueWith(Action<Task<TResult>> continuationAction, TaskContinuationOptions continuationOptions);
        public Task ContinueWith(Action<Task<TResult>> continuationAction, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler);
        public Task ContinueWith(Action<Task<TResult>> continuationAction);
        public Task ContinueWith(Action<Task<TResult>, object> continuationAction, object state, TaskScheduler scheduler);
        public Task ContinueWith(Action<Task<TResult>, object> continuationAction, object state, TaskContinuationOptions continuationOptions);
        public Task ContinueWith(Action<Task<TResult>, object> continuationAction, object state, CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler);
        public Task ContinueWith(Action<Task<TResult>, object> continuationAction, object state);
        public Task ContinueWith(Action<Task<TResult>> continuationAction, CancellationToken cancellationToken);
        public TaskAwaiter<TResult> GetAwaiter();
    }
}
