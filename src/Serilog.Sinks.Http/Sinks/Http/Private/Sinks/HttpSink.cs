﻿// Copyright 2015-2016 Serilog Contributors
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.Http.Private.Formatters;
using Serilog.Sinks.PeriodicBatching;

namespace Serilog.Sinks.Http.Private.Sinks
{
    internal class HttpSink : PeriodicBatchingSink
    {
        private static readonly string ContentType = "application/json";

        private readonly string requestUri;
        private readonly long? eventBodyLimitBytes;
        private readonly ITextFormatter formatter;

        private IHttpClient client;

        public HttpSink(
            string requestUri,
            int batchPostingLimit,
            TimeSpan period,
            long? eventBodyLimitBytes,
            FormattingType formattingType,
            IHttpClient client)
            : base(batchPostingLimit, period)
        {
            this.requestUri = requestUri ?? throw new ArgumentNullException(nameof(requestUri));
            this.eventBodyLimitBytes = eventBodyLimitBytes;
            this.client = client ?? throw new ArgumentNullException(nameof(client));

            formatter = Converter.ToFormatter(formattingType);
        }

        protected override async Task EmitBatchAsync(IEnumerable<LogEvent> events)
        {
            var payload = FormatPayload(events);
            var content = new StringContent(payload, Encoding.UTF8, ContentType);

            var result = await client
                .PostAsync(requestUri, content)
                .ConfigureAwait(false);

            if (!result.IsSuccessStatusCode)
                throw new LoggingFailedException($"Received failed result {result.StatusCode} when posting events to {requestUri}");
        }

        /// <summary>
        /// Free resources held by the sink.
        /// </summary>
        /// <param name="disposing">
        /// If true, called because the object is being disposed; if false, the object is being
        /// disposed from the finalizer.
        /// </param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                client?.Dispose();
                client = null;
            }
        }

        private string FormatPayload(IEnumerable<LogEvent> events)
        {
            var payload = new StringWriter();
            payload.Write("{\"events\":[");

            var delimStart = string.Empty;

            foreach (var logEvent in events)
            {
                var buffer = new StringWriter();
                formatter.Format(logEvent, buffer);

                if (string.IsNullOrEmpty(buffer.ToString()))
                {
                    continue;
                }

                var json = buffer.ToString();
                if (CheckEventBodySize(json))
                {
                    payload.Write(delimStart);
                    payload.Write(json);
                    delimStart = ",";
                }
            }

            payload.Write("]}");

            return payload.ToString();
        }

        private bool CheckEventBodySize(string json)
        {
            if (eventBodyLimitBytes.HasValue &&
                Encoding.UTF8.GetByteCount(json) > eventBodyLimitBytes.Value)
            {
                SelfLog.WriteLine(
                    "Event JSON representation exceeds the byte size limit of {0} set for this sink and will be dropped; data: {1}",
                    eventBodyLimitBytes,
                    json);

                return false;
            }

            return true;
        }
    }
}