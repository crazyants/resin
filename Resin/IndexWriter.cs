﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resin.IO;

namespace Resin
{
    public class IndexWriter : IDisposable
    {
        private readonly string _directory;
        private readonly IAnalyzer _analyzer;

        /// <summary>
        /// field/doc count
        /// </summary>
        private readonly ConcurrentDictionary<string, int> _docCountByField;

        /// <summary>
        /// field/trie
        /// </summary>
        private readonly Dictionary<string, LcrsTrie> _tries;

        /// <summary>
        /// fileid/file
        /// </summary>
        private readonly ConcurrentDictionary<string, DocumentWriter> _docFiles;

        private readonly List<Document> _docs;

        public IndexWriter(string directory, IAnalyzer analyzer)
        {
            _directory = directory;
            _analyzer = analyzer;
            _docFiles = new ConcurrentDictionary<string, DocumentWriter>();
            _tries = new Dictionary<string, LcrsTrie>();
            _docCountByField = new ConcurrentDictionary<string, int>();
            _docs = new List<Document>();
        }

        public void Write(IEnumerable<Document> docs)
        {
            _docs.AddRange(docs);
        }
        
        private void Write(Document doc)
        {
            var containerId = doc.Id.ToDocFileId();
            DocumentWriter writer;
            if (!_docFiles.TryGetValue(containerId, out writer))
            {
                writer = new DocumentWriter(_directory, containerId);
                _docFiles.AddOrUpdate(containerId, writer, (s, file) => file);
            }
            _docFiles[containerId].Write(doc);
        }

        private void WriteToTrie(string field, string value)
        {
            if (field == null) throw new ArgumentNullException("field");
            if (value == null) throw new ArgumentNullException("value");

            var trie = GetTrie(field);
            trie.Add(value);
        }

        private LcrsTrie GetTrie(string field)
        {
            LcrsTrie trie;
            if (!_tries.TryGetValue(field, out trie))
            {
                trie = new LcrsTrie('\0', false);
                _tries[field] = trie;
            }
            return trie;
        }

        public void Dispose()
        {
            var termDocMatrix = new Dictionary<Term, List<DocumentPosting>>();
            foreach (var doc in _docs)
            {
                Write(doc);
                var analyzed = _analyzer.AnalyzeDocument(doc);
                foreach (var term in analyzed.Terms)
                {
                    WriteToTrie(term.Key.Field, term.Key.Token);
                    List<DocumentPosting> weights;
                    if (termDocMatrix.TryGetValue(term.Key, out weights))
                    {
                        weights.Add(new DocumentPosting(analyzed.Id, (int)term.Value));
                    }
                    else
                    {
                        termDocMatrix.Add(term.Key, new List<DocumentPosting> { new DocumentPosting(analyzed.Id, (int)term.Value) });
                    }
                }
                foreach (var field in doc.Fields)
                {
                    _docCountByField.AddOrUpdate(field.Key, 1, (s, count) => count + 1);
                }
            }

            Parallel.ForEach(_tries, kvp =>
            {
                var field = kvp.Key;
                var trie = kvp.Value;
                var fileName = Path.Combine(_directory, field.ToTrieFileId() + ".tri");
                trie.Serialize(fileName);
            });

            Parallel.ForEach(_docFiles.Values, container => container.Dispose());

            var postings = new Dictionary<Term, int>(); 
            using(var fs = new FileStream(Path.Combine(_directory, "0.pos"), FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs, Encoding.Unicode))
            {
                var row = 0;
                foreach (var term in termDocMatrix)
                {
                    var json = JsonConvert.SerializeObject(term.Value, Formatting.None);
                    postings.Add(term.Key, row++);
                    writer.WriteLine(json);
                } 
            }

            var ixInfo = new IndexInfo
            {
                PostingAddressByTerm = postings,
                DocumentCount = new DocumentCount(new Dictionary<string, int>(_docCountByField))
            };
            ixInfo.Save(Path.Combine(_directory, "0.ix"));
        }
    }

    //public class Index
    //{
        
    //}

    //public class DocumentIndexer
    //{
    //    private IEnumerable<Document> _documents;

    //    public DocumentIndexer(IEnumerable<Document> documents)
    //    {
    //        _documents = documents;
    //    }

    //    public Index Create()
    //    {
            
    //    }
    //}
}