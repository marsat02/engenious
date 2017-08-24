﻿using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using ContentTool.Models;
using engenious.Content.Pipeline;
using engenious.Content.Serialization;

namespace ContentTool.Builder
{
    public class ContentBuilder
    {
        public const string FileExtension = ".ego";

        public ContentProject Project { get; set; }

        public int FailedBuilds { get; private set; }

        public bool IsBuilding { get; private set; }

        private readonly SynchronizationContext _syncContext;

        private readonly BuildCache _cache;

        private Thread _buildThread;

        public ContentBuilder(ContentProject project)
        {
            Project = project;
            _syncContext = SynchronizationContext.Current;

            _cache = BuildCache.Load(Path.Combine(Path.GetDirectoryName(project.ContentProjectPath), "obj", project.Name + ".dat"));
        }

        public void Build(ContentItem item)
        {
            _buildThread = new Thread(() => {
                IsBuilding = true;
                BuildThread(item);
                IsBuilding = false;
            });
            _buildThread.SetApartmentState(ApartmentState.STA);
            _buildThread.Start();
        }

        public void Clean()
        {
            _buildThread = new Thread(() =>
            {
                IsBuilding = true;
                CleanThread();
                IsBuilding = false;
            });
            _buildThread.SetApartmentState(ApartmentState.STA);
            _buildThread.Start();
        }

        public void Rebuild()
        {
            _buildThread = new Thread(() => {
                IsBuilding = true;

                if (Project.HasUnsavedChanges)
                    Project.Save();
                CleanThread();
                BuildThread(Project);
                IsBuilding = false;
            });
            _buildThread.SetApartmentState(ApartmentState.STA);
            _buildThread.Start();
        }

        public void Abort()
        {
            if (IsBuilding)
                _buildThread.Abort();
        }
        public void Join()
        {
            _buildThread.Join();
        }

        protected void BuildThread(ContentItem item)
        {
            FailedBuilds = 0;
            PipelineHelper.PreBuilt(Project);

            var outputDestination = Path.Combine(Path.GetDirectoryName(Project.ContentProjectPath), string.Format(Project.OutputDirectory.Replace("{Configuration}", "{0}"), Project.Configuration));

            using (var iContext = new ContentImporterContext())
            using (var pContext = new ContentProcessorContext(_syncContext))
            {
                iContext.BuildMessage += RaiseBuildMessage;
                pContext.BuildMessage += RaiseBuildMessage;
                InternalBuildItem(item, outputDestination, iContext, pContext);
            }
            _cache.Save();
        }

        protected void CleanThread()
        {
            foreach(var item in _cache.Files)
            {
                if (File.Exists(item.Value.OutputFilePath))
                    File.Delete(item.Value.OutputFilePath);
            }
        }

        protected void InternalBuildItem(ContentItem item, string outputDestination, ContentImporterContext importerContext, ContentProcessorContext processorContext)
        {
            outputDestination = Path.Combine(outputDestination, item.Name);

            var folder = item as ContentFolder;
            if(folder != null)
            {
                foreach (var child in folder.Content)
                    InternalBuildItem(child, outputDestination, importerContext, processorContext);
            }
            else
            {
                if (_cache.NeedsRebuild(item.FilePath))
                {
                    InternalBuildFile(item as ContentFile, Path.Combine(Path.GetDirectoryName(outputDestination), Path.GetFileNameWithoutExtension(item.Name) + FileExtension), importerContext, processorContext);
                    RaiseBuildMessage(this, new BuildMessageEventArgs(item.RelativePath, item.RelativePath + " built", BuildMessageEventArgs.BuildMessageType.Information));
                }
                else
                {
                    RaiseBuildMessage(this, new BuildMessageEventArgs(item.RelativePath, item.RelativePath + " skipped", BuildMessageEventArgs.BuildMessageType.Information));
                }
                importerContext.Dependencies.Clear();
                processorContext.Dependencies.Clear();
            }


        }

        protected void RaiseBuildMessage(object sender, BuildMessageEventArgs e) => BuildMessage?.Invoke(e);

        public object InternalBuildFile(ContentFile item, string destination, ContentImporterContext importerContext, ContentProcessorContext processorContext)
        {
            return BuildFile(item, destination, importerContext, processorContext, _cache);
        }
        /// <summary>
        /// Builds a ContentFile and writes it to the destinatin
        /// </summary>
        /// <param name="item">ContentFile to build</param>
        /// <param name="destination">Location to save to</param>
        /// <param name="importerContext"></param>
        /// <param name="processorContext"></param>
        /// <returns></returns>
        public static object BuildFile(ContentFile item, string destination, ContentImporterContext importerContext, ContentProcessorContext processorContext,BuildCache cache = null)
        {
            if (!File.Exists(item.FilePath)) return null;

            var dirName = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(dirName);

            var buildFile = new BuildFile(item.FilePath, destination);

            var importer = item.Importer;

            var importedFile = importer?.Import(item.FilePath, importerContext);
            if (importedFile == null) return null;

            buildFile.Dependencies.AddRange(importerContext.Dependencies);
            cache?.AddDependencies(Path.GetDirectoryName(item.FilePath), importerContext.Dependencies);

            var processor = item.Processor;

            var processedFile = processor?.Process(importedFile, item.FilePath, processorContext);
            if (processedFile == null) return null;

            buildFile.Dependencies.AddRange(processorContext.Dependencies);
            cache?.AddDependencies(Path.GetDirectoryName(item.FilePath), processorContext.Dependencies);

            var typeWriter = SerializationManager.Instance.GetWriter(processedFile.GetType());
            var outputContentFileWriter = new engenious.Content.ContentFile(typeWriter.RuntimeReaderName);

            var formatter = new BinaryFormatter();
            using (var fs = new FileStream(destination, FileMode.Create, FileAccess.Write))
            {
                formatter.Serialize(fs, outputContentFileWriter);
                var writer = new ContentWriter(fs);
                writer.WriteObject(processedFile, typeWriter);
            }

            cache?.AddFile(item.FilePath, buildFile);
            buildFile.RefreshModifiedTime();

            return processedFile;
        }

        public event BuildMessageHandler BuildMessage;
        public event BuildStatusChangedHandler BuildStatusChanged;

        public delegate void BuildMessageHandler(BuildMessageEventArgs args);
        public delegate void BuildStatusChangedHandler(BuildStatus status);

        [Flags]
        public enum BuildStatus
        {
            Clean = 1,
            Build = 2,
            Abort = 4,
            Finished = 8,
            Built = 16
        }

    }
}
