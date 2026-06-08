#nullable enable

using System;
using System.Net;

namespace Microsoft.UI.Reactor.VsExtension.Embed
{
    internal sealed class EmbedProtocolMismatchException : Exception
    {
        public EmbedProtocolMismatchException(string message)
            : base(message)
        {
        }

        public EmbedProtocolMismatchException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal sealed class EmbedDpiMismatchException : Exception
    {
        public EmbedDpiMismatchException(string message)
            : base(message)
        {
        }
    }

    internal sealed class EmbedRequestException : Exception
    {
        public EmbedRequestException(HttpStatusCode statusCode, string message, string? responseBody = null)
            : base(message)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }

        public HttpStatusCode StatusCode { get; }

        public string? ResponseBody { get; }
    }
}
