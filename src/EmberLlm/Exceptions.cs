using System.Net;

namespace EmberLlm;

/// <summary>
/// Base type for every exception EmberLlm throws. Catch this to handle all library failures.
/// </summary>
public class EmberLlmException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public EmberLlmException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public EmberLlmException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public EmberLlmException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The server answered with a non-success HTTP status code.
/// </summary>
public class EmberLlmHttpException : EmberLlmException
{
    /// <summary>Creates an exception with no message.</summary>
    public EmberLlmHttpException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public EmberLlmHttpException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public EmberLlmHttpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception describing a failed HTTP response.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="statusCode">The HTTP status code the server returned.</param>
    /// <param name="serverMessage">The error message extracted from the response body, if any.</param>
    /// <param name="responseBody">The raw response body.</param>
    public EmberLlmHttpException(string message, HttpStatusCode statusCode, string? serverMessage, string? responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
        ResponseBody = responseBody;
    }

    /// <summary>The HTTP status code the server returned.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The error message from the response body, when the server sent one in a recognized shape.</summary>
    public string? ServerMessage { get; }

    /// <summary>The raw response body, useful for diagnosing backend-specific errors.</summary>
    public string? ResponseBody { get; }

    /// <summary><see langword="true"/> for 5xx responses, which usually mean the server failed rather than the request.</summary>
    public bool IsServerError => (int)StatusCode >= 500;
}

/// <summary>
/// The server could not be reached, for example because it is not running.
/// </summary>
public class EmberLlmConnectionException : EmberLlmException
{
    /// <summary>Creates an exception with no message.</summary>
    public EmberLlmConnectionException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public EmberLlmConnectionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public EmberLlmConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A request took longer than <see cref="EmberLlmOptions.RequestTimeout"/>. Cancellation through the
/// caller's token throws <see cref="OperationCanceledException"/> instead.
/// </summary>
public class EmberLlmTimeoutException : EmberLlmException
{
    /// <summary>Creates an exception with no message.</summary>
    public EmberLlmTimeoutException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public EmberLlmTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public EmberLlmTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// No model was named on the request or in <see cref="EmberLlmOptions.DefaultModel"/>, and the server lists no models.
/// </summary>
public class NoModelAvailableException : EmberLlmException
{
    /// <summary>Creates an exception with no message.</summary>
    public NoModelAvailableException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public NoModelAvailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public NoModelAvailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
