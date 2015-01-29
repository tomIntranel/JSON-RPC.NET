﻿using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AustinHarris.JsonRpc
{
    public static class JsonRpcProcessor
    {
        public static void Process(JsonRpcStateAsync async, object context = null)
        {
            Task.Factory.StartNew((_async) =>
            {
                var tuple = (Tuple<JsonRpcStateAsync, object>)_async;
                ProcessJsonRpcState(tuple.Item1, tuple.Item2);
            }, new Tuple<JsonRpcStateAsync,object>(async,context));
            
        }

        public static void Process(string sessionId, JsonRpcStateAsync async, object context = null)
        {
            var t = Task.Factory.StartNew((_async) =>
            {
                var i = (Tuple<string, JsonRpcStateAsync, object>)_async;
                ProcessJsonRpcState(i.Item1, i.Item2, i.Item3);
            }, new Tuple<string, JsonRpcStateAsync, object>(sessionId, async, context));

        }
        internal static void ProcessJsonRpcState(JsonRpcStateAsync async, object jsonRpcContext = null)
        {
            ProcessJsonRpcState(Handler.DefaultSessionId(), async, jsonRpcContext);
        }
        internal static void ProcessJsonRpcState(string sessionId, JsonRpcStateAsync async, object jsonRpcContext = null)
        {
            async.Result = ProcessInternal(sessionId, async.JsonRpc, jsonRpcContext);
            async.SetCompleted();
        }

        public static Task<string> Process(string jsonRpc, object context = null)
        {
            return Process(Handler.DefaultSessionId(), jsonRpc, context);
        }
        public static Task<string> Process(string sessionId, string jsonRpc, object context = null)
        {
            return Task<string>.Factory.StartNew((_) =>
            {
                var tuple = (Tuple<string, string, object>) _;
                return ProcessInternal(tuple.Item1, tuple.Item2, tuple.Item3);
            }, new Tuple<string, string, object>(sessionId, jsonRpc, context));
        }

        private static string ProcessInternal(string sessionId, string jsonRpc, object jsonRpcContext)
        {
            var handler = Handler.GetSessionHandler(sessionId);

            try
            {
                if (isSingleRpc(jsonRpc))
                {
                    jsonRpc = string.Format("[{0}]", jsonRpc);
                }

                var batch =
                    Newtonsoft.Json.JsonConvert.DeserializeObject<JsonRequest[]>(jsonRpc)
                        .Select(request => new Tuple<JsonRequest, JsonResponse>(request, new JsonResponse())).ToArray();

                if (batch.Length == 0)
                {
                    return Newtonsoft.Json.JsonConvert.SerializeObject(new JsonResponse
                    {
                        Error = handler.ProcessParseException(jsonRpc,
                            new JsonRpcException(3200, "Invalid Request", "Batch of calls was empty."))
                    });
                }

                foreach (var tuple in batch)
                {
                    var jsonRequest = tuple.Item1;
                    var jsonResponse = tuple.Item2;

                    if (jsonRequest == null)
                    {
                        jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                            new JsonRpcException(-32700, "Parse error",
                                "Invalid JSON was received by the server. An error occurred on the server while parsing the JSON text."));
                    }
                    else
                    {
                        jsonResponse.Id = jsonRequest.Id;

                        if (jsonRequest.Method == null)
                        {
                            jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                                new JsonRpcException(-32600, "Invalid Request", "Missing property 'method'"));
                        }
                        else
                        {
                            var data = handler.Handle(jsonRequest, jsonRpcContext);

                            if (data == null) continue;

                            jsonResponse.Error = data.Error;
                            jsonResponse.Result = data.Result;
                        }
                    }
                }

                var responses = batch.Select(tuple => tuple.Item2)
                    .Where(resp => resp.Id != null || resp.Error != null)
                    .Select(Newtonsoft.Json.JsonConvert.SerializeObject).ToArray();

                return responses.Count() > 1 ? string.Format("[{0}]", string.Join(",", responses)) : responses.First();
            }
            catch (Exception ex)
            {
                return Newtonsoft.Json.JsonConvert.SerializeObject(new JsonResponse
                {
                    Error = handler.ProcessParseException(jsonRpc, new JsonRpcException(-32700, "Parse error", ex))
                });
            }
        }

        private static bool isSingleRpc(string json)
        {
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '{') return true;
                else if (json[i] == '[') return false;
            }
            return true;
        }
    }
}
