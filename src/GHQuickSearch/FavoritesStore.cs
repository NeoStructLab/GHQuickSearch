using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GHQuickSearch
{
    [DataContract] internal sealed class FavoritesFile
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<string> Favorites = new List<string>();
    }
    internal sealed class FavoritesStore
    {
        public const int MaximumFavorites = 256;
        public string FilePath { get; }
        public string Warning { get; private set; }
        public List<Guid> Ids { get; private set; } = new List<Guid>();
        private bool loaded;
        public bool IsLoaded => loaded;
        public FavoritesStore(string path) { FilePath = path; }
        internal static List<Guid> Reordered(IReadOnlyList<Guid> ids,Guid moving,int slot)
        {
            var list=ids.ToList();int from=list.IndexOf(moving);
            if(from<0)return list;
            slot=Math.Max(0,Math.Min(slot,list.Count));list.RemoveAt(from);
            if(from<slot)slot--;list.Insert(slot,moving);return list;
        }
        public void Load(IEnumerable<Guid> defaults)
        {
            if (loaded) return;
            loaded = true;
            Ids = defaults.Distinct().Take(MaximumFavorites).ToList();
            if (!File.Exists(FilePath)) return;
            try
            {
                using (var input = File.OpenRead(FilePath))
                {
                    if (input.Length > 65536) throw new InvalidDataException("Favorites file is too large.");
                    var data = (FavoritesFile)new DataContractJsonSerializer(typeof(FavoritesFile)).ReadObject(input);
                    if (data == null || data.Version != 1 || data.Favorites == null)
                        throw new InvalidDataException("Unsupported favorites format.");
                    Ids = data.Favorites.Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                        .Where(g => g != Guid.Empty).Distinct().Take(MaximumFavorites).ToList();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SerializationException || ex is System.Xml.XmlException)
            {
                Warning = "Could not read favorites. Defaults are shown. " + ex.Message;
            }
        }
        public void Update(IEnumerable<Guid> ids)
        {
            var next = ids.Where(g => g != Guid.Empty).Distinct().Take(MaximumFavorites).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(FavoritesFile)).WriteObject(output,
                        new FavoritesFile { Favorites = next.Select(g => g.ToString("D")).ToList() });
                    output.Flush(true);
                }
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
                else File.Move(temp, FilePath);
                Ids = next;
                Warning = null;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
