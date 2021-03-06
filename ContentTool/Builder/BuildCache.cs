﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace ContentTool.Builder
{
    [Serializable]
    public class BuildCache
    {
        public string CacheFilePath { get; private set; }

        public Dictionary<string, BuildFile> Files { get; } = new Dictionary<string, BuildFile>();

        protected BuildCache(string cacheFilePath)
        {
            CacheFilePath = cacheFilePath;
            //Files
        }

        public void AddFile(string path, BuildFile file)
        {
            Files[path] = file;
        }

        public void AddDependencies(string importDir, IEnumerable<string> dependencies)
        {
            foreach (var dependency in dependencies)
            {
                var absPath = Path.Combine(importDir, dependency);
                if (Files.ContainsKey(absPath))
                    Files[absPath].RefreshModifiedTime();
                else
                    Files.Add(absPath, new BuildFile(absPath,null));

            }
        }

        public bool NeedsRebuild(string inputPath,DateTime? parentModifiedTime=null)
        {
            if(Files.TryGetValue(inputPath, out BuildFile val))
            {
                if (val.NeedsRebuild(parentModifiedTime))
                    return true;
                foreach (var dependency in val.Dependencies)
                {
                    if (NeedsRebuild(dependency,parentModifiedTime ?? val.OutputFileModifiedTime))
                        return true;
                }
                return false;
            }
            return true;
        }

        public bool HasBuiltItems()
        {
            foreach (var file in Files)
            {
                if (file.Value.IsBuilt())
                    return true;
            }

            return false;
        }

        public void Clean()
        {
            
        }

        public void Clear()
        {
            Files.Clear();
        }

        /// <summary>
        /// Saves the cache to the original location
        /// </summary>
        public void Save()
        {
            Save(CacheFilePath);
        }

        /// <summary>
        /// Saves the cache to the specified location
        /// </summary>
        /// <param name="path"></param>
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    var formatter = new BinaryFormatter();
                    formatter.Serialize(fs, this);
                }
                CacheFilePath = path;
            }
            catch
            {
                // ignored
            }
        }

        /// <summary>
        /// Loads the cache from the specified location or returns a new one if the file could not be found
        /// </summary>
        /// <param name="cacheFilePath">Location of the cache file</param>
        /// <returns>The build cache</returns>
        public static BuildCache Load(string cacheFilePath)
        {
            if (!File.Exists(cacheFilePath))
            {
                var cache = new BuildCache(cacheFilePath);

                return cache;
            }

            try
            {
                using (var fs = new FileStream(cacheFilePath, FileMode.Open, FileAccess.Read))
                {
                    var formatter = new BinaryFormatter();
                    return (BuildCache)formatter.Deserialize(fs);
                }
            }
            catch
            {
                try
                {
                    File.Delete(cacheFilePath);
                }
                catch
                {
                    // ignored
                }
                return Load(cacheFilePath);
            }
        }
    }
}
