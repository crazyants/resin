﻿using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Sir.HttpServer.Controllers
{
    [Route("io")]
    public class IOController : Controller
    {
        private readonly PluginsCollection _plugins;
        private readonly StreamWriter _log;

        public IOController(PluginsCollection plugins)
        {
            _plugins = plugins;
            _log = Logging.CreateWriter("iocontroller");
        }

        [HttpDelete("delete/{*collectionId}")]
        public async Task<IActionResult> Delete(string collectionId, string q)
        {
            throw new NotImplementedException();
        }

        [HttpPost("{*collectionId}")]
        public async Task<IActionResult> Post(string collectionId, string id)
        {
            if (collectionId == null)
            {
                throw new ArgumentNullException(nameof(collectionId));
            }

            var collection = collectionId.ToHash();
            var writer = _plugins.Get<IWriter>(Request.ContentType);

            if (writer == null)
            {
                return StatusCode(415); // Media type not supported
            }

            var payload = Request.Body;
            long recordId;

            try
            {
                var mem = new MemoryStream();

                await payload.CopyToAsync(mem);

                if (id == null)
                {
                    recordId = await writer.Write(collection, mem);
                }
                else
                {
                    recordId = long.Parse(id);

                    writer.Append(collection, recordId, mem);
                }
            }
            catch (Exception ew)
            {
                throw ew;
            }

            Response.Headers.Add(
                "Location", new Microsoft.Extensions.Primitives.StringValues(string.Format("{0}/io/{1}?id={2}", Request.Host, collectionId, recordId)));

            return StatusCode(201); // Created
        }

        [HttpGet("{*collectionId}")]
        [HttpPut("{*collectionId}")]
        public HttpResponseMessage Get(string collectionId)
        {
            var mediaType = Request.ContentType ?? string.Empty;
            var queryParser = _plugins.Get<IHttpQueryParser>(mediaType);
            var reader = _plugins.Get<IReader>();
            var tokenizer = _plugins.Get<ITokenizer>(mediaType);

            if (queryParser == null || reader == null || tokenizer == null)
            {
                throw new NotSupportedException();
            }

            var query = queryParser.Parse(collectionId, Request, tokenizer);
            var result = reader.Read(query);
            var response = new HttpResponseMessage(HttpStatusCode.OK);

            response.Content = new StreamContent(result.Data);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(result.MediaType);

            return response;
        }
    }
}