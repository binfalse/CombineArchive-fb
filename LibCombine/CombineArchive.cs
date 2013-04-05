﻿using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System;

namespace LibCombine
{
    public class CombineArchive : IEnumerable<Entry>
    {
        const string omexNs = "http://identifiers.org/combine.specifications/omex-manifest";

        public List<Entry> Entries { get; set; }
        
        public List<OmexDescription> Descriptions { get; set; }

        public string ArchiveFileName { get; set; }

        public string BaseDir { get; set; }

        public string MainFile { get; set; }

        public Entry AddEntry(string fileName, string format, OmexDescription description)
        {
            if (string.IsNullOrWhiteSpace(BaseDir))
                BaseDir = Path.GetTempPath();

            if (!File.Exists(fileName))
                return null;

            var name = Path.GetFileName(fileName);
            string tempFile = Path.Combine(BaseDir, name);

            if (File.Exists(tempFile))
            {
                File.Delete(tempFile); 
            }
            File.Copy(fileName, tempFile);

            var entry = new Entry
            {
                Archive = this,
                Format = format,
                Location = name
            };

            Entries.Add(entry);

            if (description != null && !description.Empty)
            {
                description.About = name;
                Descriptions.Add(description);
            }


            return entry;


        }

        public List<Entry> GetEntriesWithFormat(string format)
        {
            UpdateRefs();

            var files = Entries.Where(e => e.Format == format || e.Format == Entry.KnownFormats[format.ToLowerInvariant()]).ToList();
            return files;
        }

        public int GetNumEntriesWithFormat(string format)
        {
            return GetEntriesWithFormat(format).Count;
        }

        public bool HasEntriesWithFormat(string format)
        {
            return GetEntriesWithFormat(format).Any();
        }

        public List<string> GetFilesWithFormat(string format)
        {
            var files = GetEntriesWithFormat(format).Where(e => e.GetLocalFileName() != null).Select(s => s.GetLocalFileName()).ToList();
            return files;
        }

        public int GetNumFilesWithFormat(string format)
        {
            return GetFilesWithFormat(format).Count;
        }

        public bool HasFilesWithFormat(string format)
        {
            return GetFilesWithFormat(format).Any();
        }


        /// <summary>
        /// Constructs a new archive document from the given filename
        /// </summary>
        /// <param name="fileName">Name of the file to load.</param>
        /// <returns>the document representing the file</returns>
        public static CombineArchive FromFile(string fileName)
        {
            var result = new CombineArchive();
            result.InitializeFromArchive(fileName);
            return result;
        }

        private void ParseManifest(string fileName)
        {
            var doc = new XmlDocument();
            doc.Load(fileName);
            var list = doc.DocumentElement.GetElementsByTagName("content", omexNs);
            foreach (XmlNode xmlNode in list)
            {
                var element = (XmlElement)xmlNode;
                var location = element.GetAttribute("location");
                var format = element.GetAttribute("format");
                Entries.Add(new Entry
                {
                    Archive = this, 
                    Location = location,
                    Format = format
                });
            }

            var descEntries = Entries.Where(s => s.Format == Entry.KnownFormats["omex"]).ToList();
            foreach (var entry in descEntries)
            {
                string entryLocation = entry.Location;
                if (entryLocation.Contains("http://"))
                    continue;
                Descriptions.AddRange(OmexDescription.ParseFile(Path.Combine(BaseDir, entryLocation)));
            }

            Entries.RemoveAll(e => e.Format == Entry.KnownFormats["omex"] || e.Format == Entry.KnownFormats["manifest"]);

            if (Descriptions.Count > 0)
            {
                MainFile = Descriptions[0].About;
            }

        }

        public void InitializeFromArchive(string fileName)
        {
            BaseDir = Util.UnzipArchive(fileName);
            ParseManifest(Path.Combine(BaseDir, "manifest.xml"));
            ArchiveFileName = fileName;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CombineArchive"/> class.
        /// </summary>
        public CombineArchive()
        {
            ArchiveFileName = "untitled.omex";
            Entries = new List<Entry>();
            Descriptions = new List<OmexDescription>();
        }

        /// <summary>
        /// Writes the manifest to.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        private void WriteManifestTo(string fileName)
        {
            File.WriteAllText(fileName, ToManifest());
        }
        
        internal void UpdateRefs()
        {
            Entries.ForEach(entry => entry.Archive = this);
        }

        /// <summary>
        /// Saves to.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        public void SaveTo(string fileName)
        {
            var manifestFile = Path.Combine(BaseDir, "manifest.xml");
            
            Entries.RemoveAll(e => e.Location == manifestFile || e.Format == Entry.KnownFormats["omex"] && e.GetLocalFileName() != null);
            
            Entries.Insert(0, new Entry { Location = manifestFile, Format = Entry.KnownFormats["manifest"] });            

            UpdateRefs();


            for (int i = 0; i < Descriptions.Count; i++)
            {
                var metadataFile = Path.Combine(BaseDir, string.Format("manifest{0}.xml", i));
                File.WriteAllText(metadataFile, Descriptions[i].ToXML());
                Entries.Add(new Entry { Archive = this, Location = metadataFile, Format = Entry.KnownFormats["omex"] });
            }

            WriteManifestTo(manifestFile);
            

            var fileNames = Entries.Select(e => e.GetLocalFileName()).Where(s => s != null).ToList();
            

            Util.CreateArchive(fileName, fileNames, BaseDir);
        }

        /// <summary>
        /// Converts it to the manifest, 
        /// </summary>
        /// <returns></returns>
        public string ToManifest()
        {
            XNamespace ns = Entry.KnownFormats["manifest"];
            var root = new XElement(ns + "omexManifest");
            foreach (var entry in Entries)
            {
                root.Add(
                    new XElement(ns +"content", 
                        new XAttribute("location", 
                            entry.Location
                            .Replace(BaseDir, "./")
                            .Replace("././", "./")
                            .Replace("./\\", "./")
                            ), 
                        new XAttribute("format", entry.Format)));
            }
            var srcTree = new XDocument(root);
            return 
                "<?xml version='1.0' encoding='utf-8' standalone='yes'?>\n" + 
                srcTree.ToString();
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IEnumerator`1" /> that can be used to iterate through the collection.
        /// </returns>
        public IEnumerator<Entry> GetEnumerator()
        {
            return Entries.GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator that iterates through a collection.
        /// </summary>
        /// <returns>
        /// An <see cref="T:System.Collections.IEnumerator" /> object that can be used to iterate through the collection.
        /// </returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
