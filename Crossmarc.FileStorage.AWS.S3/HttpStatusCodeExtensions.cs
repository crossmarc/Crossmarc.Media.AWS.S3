using System;
using System.Net;

namespace Crossmarc.FileStorage.AWS.S3
{
    public static class HttpStatusCodeExtensions
    {
        public static void EnsureSuccessStatusCode(this HttpStatusCode statusCode, string errorMessage = null)
        {
            if (!statusCode.IsSuccessStatusCode())
            {
                throw new InvalidOperationException(string.Format("{0} {1} ({2})", errorMessage, (int)statusCode, statusCode));
            }
        }

        public static bool IsSuccessStatusCode(this HttpStatusCode statusCode)
        {
            // Successful codes are 2xx
            return statusCode >= HttpStatusCode.OK && statusCode < HttpStatusCode.MultipleChoices;
        }
    }
}
