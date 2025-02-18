﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.GraphModel;
using Microsoft.VisualStudio.GraphModel.CodeSchema;
using Microsoft.VisualStudio.GraphModel.Schemas;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace CodeTourVS
{

    [GraphProvider(Name = Vsix.Name)]
    public partial class CodeTourGraphProvider : IGraphProvider
    {
        private const string _iconStep = "step";

        private static bool _imagesRegistered;

        [Import(typeof(SVsServiceProvider))]
        private IServiceProvider ServiceProvider { get; set; }

        public void BeginGetGraphData(IGraphContext context)
        {
            if (context.Direction == GraphContextDirection.Self &&
                context.RequestedProperties.Contains(DgmlNodeProperties.ContainsChildren))
            {
                MarkThatNodesHaveChildren(context);
                RegisterImagesAsync().FileAndForget(nameof(CodeTourGraphProvider.BeginGetGraphData));
                context.OnCompleted();
            }

            else if (context.Direction == GraphContextDirection.Self ||
                (context.Direction == GraphContextDirection.Contains && context.InputNodes.ElementAt(0).HasCategory(CodeTourSchema.Tour)))
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await PopulateChildrenOfNodesAsync(context).ConfigureAwait(false);
                    }
                    catch
                    { 
                    }
                    finally
                    {
                        context.OnCompleted();
                    }
                });
            }
        }

        private static void MarkThatNodesHaveChildren(IGraphContext context)
        {
            using (var scope = new GraphTransactionScope())
            {
                foreach (GraphNode node in context.InputNodes.Where(IsCodeTourFile))
                {
                    node.SetValue(DgmlNodeProperties.ContainsChildren, true);
                    node.AddCategory(CodeTourSchema.Tour);
                }

                scope.Complete();
            }
        }

        private async Task PopulateChildrenOfNodesAsync(IGraphContext context)
        {
            using (var scope = new GraphTransactionScope())
            {
                foreach (GraphNode tourNode in context.InputNodes.Where(IsCodeTourFile))
                {
                    Graph graph = tourNode.Owner;
                    var fileName = tourNode.Id.GetNestedValueByName<Uri>(CodeGraphNodeIdName.File).LocalPath;
                    var tourFolder = Path.GetDirectoryName(fileName);
                    CodeTourManager manager = await CodeTourManager.FromFolderAsync(tourFolder, context.CancelToken);
                    CodeTour tour = manager.GetTour(fileName);
                    var padding = tour.Steps.Count().ToString().Length;
                    var stepNo = 1;

                    foreach (Step step in tour.Steps)
                    {
                        try
                        {
                            var uniqueName = $"#{stepNo++.ToString().PadLeft(padding, '0')} - {step.Title}";

                            GraphNodeId valueId = tourNode.Id + GraphNodeId.GetPartial(CodeTourSchema.StepValueName, uniqueName);
                            GraphNode stepNode = graph.Nodes.GetOrCreate(valueId, uniqueName, CodeTourSchema.Step);

                            EnsureAbsolutePath(step);
                            var loc = new SourceLocation(step.AbsoluteFile, new Position(step.Line, 0));
                            stepNode.SetValue(CodeTourSchema.StepLocation, loc);
                            stepNode[DgmlNodeProperties.Icon] = _iconStep;

                            GraphLink link = graph.Links.GetOrCreate(tourNode, stepNode, null, CodeTourSchema.TourToStepLink);
                            link.SetValue("step", step);
                            context.OutputNodes.Add(stepNode);
                        }
                        catch (Exception ex)
                        {
                            context.ReportError(ex);
                        }
                        finally
                        {
                            context.ReportProgress(stepNo - 1, tour.Steps.Count(), null);
                        }
                    }
                }
                scope.Complete();
            }

            context.OnCompleted();
        }

        private void EnsureAbsolutePath(Step step)
        {
            if (!File.Exists(step.AbsoluteFile) && step.AbsoluteFile.Contains(Path.GetTempPath()))
            {
                PackageUtilities.EnsureOutputPath(step.AbsoluteFile);
            }
        }

        private static bool IsCodeTourFile(GraphNode node)
        {
            var localPath = node.Id.GetNestedValueByName<Uri>(CodeGraphNodeIdName.File)?.OriginalString;

            return node.HasCategory(CodeNodeCategories.File) &&
                   localPath.EndsWith(".tour");
        }

        protected async Task RegisterImagesAsync()
        {
            if (_imagesRegistered)
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            IVsImageService2 imageService = await AsyncServiceProvider.GlobalProvider.GetServiceAsync<SVsImageService, IVsImageService2>();
            IVsUIObject icon = await KnownMonikers.Favorite.ToUiObjectAsync(16);

            imageService.Add(_iconStep, icon);
            _imagesRegistered = true;
        }

        public T GetExtension<T>(GraphObject graphObject, T previous) where T : class
            => typeof(T) == typeof(IGraphNavigateToItem) ? new CodeTourGraphNodeNavigator() { serviceProvider = ServiceProvider } as T : null;

        public Graph Schema => null; // only for architecture explorer

        public IEnumerable<GraphCommand> GetCommands(IEnumerable<GraphNode> nodes)
        {
            return new[] {
                new GraphCommand(GraphCommandDefinition.Contains, new[] { CodeTourSchema.Tour}),
                new GraphCommand(GraphCommandDefinition.Contains, new[] { CodeTourSchema.Step}, trackChanges: true),
            };
        }
    }
}
