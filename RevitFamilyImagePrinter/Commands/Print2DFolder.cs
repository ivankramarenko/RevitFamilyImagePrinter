﻿using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using RevitFamilyImagePrinter.Infrastructure;
using System.Linq;
using Autodesk.Revit.DB.Events;

namespace RevitFamilyImagePrinter.Commands
{
	[Transaction(TransactionMode.Manual)]
	class Print2DFolder : IExternalCommand
	{
		#region Properties
		public UserImageValues UserValues { get; set; } = new UserImageValues();
		public DirectoryInfo UserFolderFrom { get; set; }
		public DirectoryInfo UserFolderTo { get; set; }
		#endregion

		#region Variables
		private UIDocument _uiDoc;
		private readonly Logger _logger = App.Logger;
		#endregion

		#region Constants
		private const int windowHeightOffset = 40;
		private const int windowWidthOffset = 10;
		private const int maxSizeLength = 2097152;
		#endregion

		public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
		{
			PrintProgressHelper progressHelper = null;
			UIApplication uiApp = commandData.Application;
			uiApp.Application.FailuresProcessing += Application_FailuresProcessing;
			try
			{
				_uiDoc = uiApp.ActiveUIDocument;
				var initProjectPath = _uiDoc.Document.PathName;
				PrintHelper.CreateEmptyProject(uiApp.Application);

				DirectoryInfo familiesFolder =
					PrintHelper.SelectFolderDialog($"{App.Translator.GetValue(Translator.Keys.folderDialogFromTitle)}");
				if (familiesFolder == null)
					return Result.Cancelled;

				UserFolderTo =
					PrintHelper.SelectFolderDialog($"{App.Translator.GetValue(Translator.Keys.folderDialogToTitle)}");
				if (UserFolderTo == null)
					return Result.Cancelled;

				UserValues =
					PrintHelper.ShowOptionsDialog(_uiDoc, windowHeightOffset, windowWidthOffset, false, false, false);
				if (UserValues == null)
					return Result.Failed;

				var families = GetFamilyFilesFromFolder(familiesFolder);
				if (families is null || families.Count < 1)
					return Result.Failed;

				progressHelper = new PrintProgressHelper(familiesFolder,
					$"{App.Translator.GetValue(Translator.Keys.textBlockProcessCreatingProjects)}");
				progressHelper.Show();
				progressHelper.SubscribeOnLoadedFamily(uiApp);
				progressHelper.SetProgressBarMaximum(families.Count);

				UserFolderFrom = new DirectoryInfo(Path.Combine(familiesFolder.FullName,
					App.Translator.GetValue(Translator.Keys.folderProjectsName)));
				foreach (var i in families)
				{
					try
					{
						PathData pathData = new PathData()
						{
							FamilyPath = i.FullName,
							ProjectsPath = UserFolderFrom.FullName,
							ImagesPath = UserFolderTo.FullName
						};
						ProjectHelper.CreateProjectsFromFamily(_uiDoc, pathData, UserValues);
					}
					catch (Exception exc)
					{
						PrintHelper.ProcessError(exc,
							$"{App.Translator.GetValue(Translator.Keys.errorMessage2dFolderPrinting)}", _logger, false);
					}
				}

				if (!string.IsNullOrEmpty(initProjectPath) && File.Exists(initProjectPath))
					_uiDoc = PrintHelper.OpenDocument(_uiDoc, initProjectPath);
				else
					_uiDoc = PrintHelper.OpenDocument(_uiDoc, App.DefaultProject);
			}
			catch (Exception exc)
			{
				PrintHelper.ProcessError(exc,
					$"{App.Translator.GetValue(Translator.Keys.errorMessage2dFolderPrinting)}", _logger);
				return Result.Failed;
			}
			finally
			{
				uiApp.Application.FailuresProcessing -= Application_FailuresProcessing;
				progressHelper?.Close();
			}
			return Result.Succeeded;
		}

		private void Application_FailuresProcessing(object sender, FailuresProcessingEventArgs e)
		{
			FailuresAccessor failuresAccessor = e.GetFailuresAccessor();
			failuresAccessor.DeleteAllWarnings();
		}

		private List<FileInfo> GetFamilyFilesFromFolder(DirectoryInfo familiesFolder)
		{
			try
			{
				return ProjectHelper.GetFamilyFilesFromFolder(familiesFolder);
			}
			catch (Exception exc)
			{
				PrintHelper.ProcessError(exc,
					$"{App.Translator.GetValue(Translator.Keys.errorMessageFamiliesRetrieving)}", App.Logger);
				return null;
			}
		}
	}
}
