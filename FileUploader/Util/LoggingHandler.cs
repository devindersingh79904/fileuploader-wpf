using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileUploadClient.Wpf.Util;

namespace FileUploadClient.Wpf.Util
{
    /// <summary>
    /// Intercepts EVERY HttpClient request/response and logs:
    /// - Method + URL
    /// - Request body (if any)
    /// - Response status + body (and restores it so callers can read it)
    /// </summary>
    public sealed class LoggingHandler : DelegatingHandler
    {
        public LoggingHandler(HttpMessageHandler inner) : base(inner) { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // --- Request ---
            NetLog.Line($"REQUEST  {request.Method} {request.RequestUri}");

            if (request.Content != null)
            {
                string reqBody = await request.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(reqBody))
                    NetLog.Line($"Request Body:\n{NetLog.Trunc(reqBody)}");
            }

            // Send
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

            // --- Response ---
            string respText = "";
            if (response.Content != null)
            {
                respText = await response.Content.ReadAsStringAsync(cancellationToken);
                // Replace the content so downstream can read it again
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
                response.Content = new StringContent(respText ?? "", Encoding.UTF8, mediaType);
            }

            NetLog.Line($"RESPONSE ({(int)response.StatusCode}) {response.StatusCode}");
            if (!string.IsNullOrWhiteSpace(respText))
                NetLog.Line($"Response Body:\n{NetLog.Trunc(respText)}");

            return response;
        }
    }
}
