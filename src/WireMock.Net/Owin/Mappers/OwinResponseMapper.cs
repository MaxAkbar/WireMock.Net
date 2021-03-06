﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WireMock.Handlers;
using WireMock.Http;
using WireMock.Util;
using WireMock.Validation;
#if !USE_ASPNETCORE
using IResponse = Microsoft.Owin.IOwinResponse;
#else
using Microsoft.AspNetCore.Http;
using IResponse = Microsoft.AspNetCore.Http.HttpResponse;
#endif

namespace WireMock.Owin.Mappers
{
    /// <summary>
    /// OwinResponseMapper
    /// </summary>
    public class OwinResponseMapper : IOwinResponseMapper
    {
        private readonly IFileSystemHandler _fileSystemHandler;
        private readonly Encoding _utf8NoBom = new UTF8Encoding(false);

        // https://msdn.microsoft.com/en-us/library/78h415ay(v=vs.110).aspx
#if !USE_ASPNETCORE
        private static readonly IDictionary<string, Action<IResponse, WireMockList<string>>> ResponseHeadersToFix = new Dictionary<string, Action<IResponse, WireMockList<string>>>(StringComparer.OrdinalIgnoreCase) {
#else
        private static readonly IDictionary<string, Action<IResponse, WireMockList<string>>> ResponseHeadersToFix = new Dictionary<string, Action<IResponse, WireMockList<string>>>(StringComparer.OrdinalIgnoreCase) {
#endif
            { HttpKnownHeaderNames.ContentType, (r, v) => r.ContentType = v.FirstOrDefault() }
        };

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="fileSystemHandler">The IFileSystemHandler.</param>
        public OwinResponseMapper(IFileSystemHandler fileSystemHandler)
        {
            Check.NotNull(fileSystemHandler, nameof(fileSystemHandler));

            _fileSystemHandler = fileSystemHandler;
        }

        /// <inheritdoc cref="IOwinResponseMapper.MapAsync"/>
        public async Task MapAsync(ResponseMessage responseMessage, IResponse response)
        {
            if (responseMessage == null)
            {
                return;
            }

            response.StatusCode = responseMessage.StatusCode;

            byte[] bytes = null;
            switch (responseMessage.BodyData?.DetectedBodyType)
            {
                case BodyType.String:
                    bytes = (responseMessage.BodyData.Encoding ?? _utf8NoBom).GetBytes(responseMessage.BodyData.BodyAsString);
                    break;

                case BodyType.Json:
                    Formatting formatting = responseMessage.BodyData.BodyAsJsonIndented == true ? Formatting.Indented : Formatting.None;
                    string jsonBody = JsonConvert.SerializeObject(responseMessage.BodyData.BodyAsJson, new JsonSerializerSettings { Formatting = formatting, NullValueHandling = NullValueHandling.Ignore });
                    bytes = (responseMessage.BodyData.Encoding ?? _utf8NoBom).GetBytes(jsonBody);
                    break;

                case BodyType.Bytes:
                    bytes = responseMessage.BodyData.BodyAsBytes;
                    break;

                case BodyType.File:
                    bytes = _fileSystemHandler.ReadResponseBodyAsFile(responseMessage.BodyData.BodyAsFile);
                    break;
            }

            SetResponseHeaders(responseMessage, response);

            if (bytes != null)
            {
                await response.Body.WriteAsync(bytes, 0, bytes.Length);
            }
        }

        private void SetResponseHeaders(ResponseMessage responseMessage, IResponse response)
        {
            // Set headers
            foreach (var pair in responseMessage.Headers)
            {
                if (ResponseHeadersToFix.ContainsKey(pair.Key))
                {
                    ResponseHeadersToFix[pair.Key]?.Invoke(response, pair.Value);
                }
                else
                {
                    // Check if this response header can be added (#148 and #227)
                    if (!HttpKnownHeaderNames.IsRestrictedResponseHeader(pair.Key))
                    {
#if !USE_ASPNETCORE
                        response.Headers.AppendValues(pair.Key, pair.Value.ToArray());
#else
                        response.Headers.Append(pair.Key, pair.Value.ToArray());
#endif
                    }
                }
            }
        }
    }
}