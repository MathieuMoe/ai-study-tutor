using TutorForge.Core.Models;

namespace TutorForge.Core.Abstractions;

/// <summary>An AI tutor that answers the student, streaming the answer piece by piece.</summary>
public interface ITutor
{
    /// <summary>Human-readable name of the engine, shown in the UI ("Claude" or "Demo").</summary>
    string EngineName { get; }

    IAsyncEnumerable<string> StreamReplyAsync(TutorRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Raised when the tutor cannot answer (AI service error, ...).</summary>
public sealed class TutorException : Exception
{
    public TutorException() { }

    public TutorException(string message) : base(message) { }

    public TutorException(string message, Exception innerException) : base(message, innerException) { }
}
