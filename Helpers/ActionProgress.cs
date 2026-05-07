using System;

namespace YellowInside.Helpers;

public sealed class ActionProgress<TProgress>(Action<TProgress> progressAction) : IProgress<TProgress>
{
    private readonly Action<TProgress> _progressAction = progressAction ?? throw new ArgumentNullException(nameof(progressAction));

    public void Report(TProgress value) => _progressAction(value);
}
