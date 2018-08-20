﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using HtmlAgilityPack;
using Sir.Core;

namespace Sir.HttpServer.Features
{
    public class CrawlQueue : IDisposable
    {
        private readonly ProducerConsumerQueue<Uri> _queue;
        private readonly PluginsCollection _plugins;
        private readonly HashSet<string> _history;
        private readonly StreamWriter _log;

        public (Uri uri, string title) LastProcessed { get; private set; }

        public CrawlQueue(PluginsCollection plugins)
        {
            _queue = new ProducerConsumerQueue<Uri>(Submit, 100);
            _plugins = plugins;
            _history = new HashSet<string>();
            _log = new StreamWriter(
                File.Open("crawlqueue.log", FileMode.Append, FileAccess.Write, FileShare.Read));
        }

        public void Enqueue(Uri uri)
        {
            _queue.Enqueue(uri);
        }

        private void Submit(Uri uri)
        {
            if (!_history.Add(uri.Host))
            {
                return;
            }

            var robotTxt = GetWebString(new Uri(string.Format("{0}://{1}/robots.txt", uri.Scheme, uri.Host)));
            var allowed = true; ;

            if (robotTxt != null)
            {
                var robotRules = GetForbiddenUrls(robotTxt);
                var uriStr = uri.ToString();

                foreach (var rule in robotRules)
                {
                    if (uriStr.Contains(rule))
                    {
                        allowed = false;
                        break;
                    }
                }
            }

            if (!allowed)
            {
                _log.Log(string.Format("url forbidden by robot.txt {0}", uri));

                return;
            }

            var str = GetWebString(uri);

            if (str == null)
            {
                return;
            }

            try
            {
                var html = new HtmlDocument();

                html.LoadHtml(str);

                var doc = Parse(html, uri);

                var document = new Dictionary<string, object>();

                document["_site"] = uri.Host;
                document["_url"] = uri.ToString().Replace(uri.Scheme + "://", string.Empty);
                document["body"] = doc.body;
                document["title"] = doc.title;
                document["_created"] = DateTime.Now.ToBinary();

                var writers = _plugins.All<IWriter>("*").ToList();

                foreach (var writer in writers)
                {
                    writer.Write("www", new[] { document });
                }

                LastProcessed = (uri, (string)document["title"]);
            }
            catch (Exception ex)
            {
                _log.Log(string.Format("error processing {0} {1}", uri, ex));
            }
        }

        private string GetWebString(Uri uri)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(uri);
                req.Headers.Add("User-Agent", "gogorobot/didyougogo.com");
                req.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;");

                using (var response = (HttpWebResponse)req.GetResponse())
                using (var content = response.GetResponseStream())
                using (var reader = new StreamReader(content))
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        _log.Log(string.Format("bad request: {0} response: {1}", uri, response.StatusCode));

                        return null;
                    }

                    _log.Log(string.Format("requested: {0}", uri));

                    return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                _log.Log(string.Format("request failed: {0} {1}", uri, ex));

                return null;
            }
        }

        private static HashSet<string> GetForbiddenUrls(string robotTxt)
        {
            var result = new HashSet<string>();

            foreach (var line in robotTxt.Split('\r', '\n'))
            {
                var parts = line.ToLower().Split(':');

                if (parts[0].Trim() == "disallow")
                {
                    var rule = parts[1].Trim(' ', '?').Replace("/*", string.Empty);

                    if (rule!="/")
                        result.Add(rule);
                }
            }

            return result;
        }

        private (string title, string body) Parse(HtmlDocument htmlDocument, Uri owner)
        {
            var title = WebUtility.HtmlDecode(htmlDocument.DocumentNode.SelectNodes("//title").First().InnerText);
            var root = htmlDocument.DocumentNode.SelectNodes("//body").First();
            var txtNodes = root.Descendants().Where(x =>
                x.Name == "#text" &&
                (x.ParentNode.Name != "script") &&
                (!string.IsNullOrWhiteSpace(x.InnerText))
            ).ToList();

            var ownerUrl = owner.Host;
            var txt = txtNodes.Select(x => WebUtility.HtmlDecode(x.InnerText));
            var body = string.Join("\r\n", txt);
            var links = htmlDocument.DocumentNode.SelectNodes("//a[@href]")
                .Select(x => x.Attributes["href"] == null ? null : x.Attributes["href"].Value)
                .Where(x => (x != null && x.StartsWith("https://") && (!x.Contains(ownerUrl))))
                .ToList();

            foreach (var url in links)
            {
                _queue.Enqueue(new Uri(url));
            }

            return (title, body);
        }

        public void Dispose()
        {
            _queue.Dispose();
            _log.Dispose();
        }
    }
}
