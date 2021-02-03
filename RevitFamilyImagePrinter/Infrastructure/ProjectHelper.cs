using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace RevitFamilyImagePrinter.Infrastructure
{
	public static class ProjectHelper
	{
		/// <summary>
		/// Creates .rvt project from the specified .rfa file. Can print each .rvt file as an image.
		/// </summary>
		/// <param name="uiDoc">Autodesk.Revit.UI.UIDocument to manipulate in nested functions.</param>
		/// <param name="pathData">Paths to .rfa, .rvt and image files directories.</param>
		/// <param name="userValues">User print values that define printed image parameters. If null, projects won't be printed.</param>
		/// <param name="is3D">Defines whether images will be printed in isometric view</param>
		/// <returns>Returns a collection of symbols created during family processing.</returns>
		public static IEnumerable<string> CreateProjectsFromFamily(UIDocument uiDoc, PathData pathData, UserImageValues userValues = null, bool is3D = false)
		{
			Document doc = uiDoc.Document;
			RemoveExcessFamilies(doc);
			List<string> allSymbols = new List<string>();
			FileInfo familyInfo = new FileInfo(pathData.FamilyPath);
			FamilyData data = new FamilyData()
			{
				FamilyName = familyInfo.Name.Remove(familyInfo.Name.LastIndexOf('.'), 4),
				FamilyPath = familyInfo.FullName
			};
			Family family = LoadFamily(uiDoc, familyInfo);
			if (family == null) return null;

			if (!Directory.Exists(pathData.ProjectsPath))
				Directory.CreateDirectory(pathData.ProjectsPath);

			ISet<ElementId> familySymbolId = family.GetFamilySymbolIds();
			foreach (ElementId id in familySymbolId)
			{
				Element element = family.Document.GetElement(id);
				data.FamilySymbols.Add(element as FamilySymbol);
			}

			foreach (var symbol in data.FamilySymbols)
			{
				Document familyDoc = doc.EditFamily(family);
				FamilyManager familyManager = familyDoc.FamilyManager;
				FamilyTypeSet familyTypes = familyManager.Types;
				FamilyType neededType = GetFamilyType(familyTypes, symbol);

				string typeId = string.Empty;
				if (!(neededType is null))
					typeId = GetFamilyParameterValue(familyManager, neededType, "Bauteil-ID");
				string nameProject = typeId;
				if (string.IsNullOrEmpty(typeId)) nameProject = $"{data.FamilyName}&{symbol.Name}";
				//var nameProject = $"{data.FamilyName}&{symbol.Name}";

				allSymbols.Add(nameProject);

				string pathProject = Path.Combine(pathData.ProjectsPath, $"{PrintHelper.CorrectFileName(nameProject)}.rvt");

				RemoveExistingInstances(doc, symbol.Id);

				try
				{
					InsertInstanceIntoProject(uiDoc, symbol);
					CreateProjectFromFamilySymbol(uiDoc, symbol, pathProject);
					if (userValues != null)
					{
						PrintProject(uiDoc, userValues, pathData.ImagesPath, is3D);
					}
				}
				catch (Autodesk.Revit.Exceptions.InvalidOperationException exc)
				{
					string errorMessage = $"File {pathProject} already exists!";
					PrintHelper.ProcessError(exc, errorMessage, App.Logger, false);
				}

				var wallInstances = new FilteredElementCollector(doc)
					.OfClass(typeof(Wall))
					.ToElements();
				foreach (var i in wallInstances)
				{
					DeleteElementCommit(doc, i);
				}

				//var markers = new FilteredElementCollector(doc)
				//	.OfClass(typeof(ElevationMarker))
				//	.ToElements();
				//foreach (var i in markers)
				//{
				//	DeleteElementCommit(doc, i);
				//}

				DeleteElementCommit(doc, symbol);
			}

			DeleteElementCommit(doc, family);
			return allSymbols;
		}

		private static FamilyType GetFamilyType(FamilyTypeSet familyTypes, FamilySymbol symbol)
		{
			foreach (FamilyType familyType in familyTypes)
			{
				if (familyType.Name.Equals(symbol.Name)) return familyType;
			}
			return null;
		}

		public static string GetFamilyParameterValue(FamilyManager familyManager, FamilyType type, string parameterStr)
		{
			FamilyParameter parameter = null;
			try
			{
				parameter = familyManager.get_Parameter(parameterStr);
			}
			catch (Exception e)
			{
				Debug.WriteLine($"Error retrieving {parameterStr} -> {e.Message}\n");
				return null;
			}

			if (parameter is null) return null;

			string result = type.AsValueString(parameter);
			switch (parameter.StorageType)
			{
				case StorageType.Double:
					result = type.AsDouble(parameter).ToString();
					break;
				case StorageType.Integer:
					if (parameter.Definition.ParameterType == ParameterType.YesNo)
						result = type.AsInteger(parameter) == 0 ? "false" : "true";
					else
						result = type.AsInteger(parameter).ToString();
					break;
				case StorageType.String:
					result = type.AsString(parameter);
					break;
				case StorageType.ElementId:
					ElementId id = type.AsElementId(parameter);
					result = id.IntegerValue.ToString();
					break;
			}
			return result;
		}

		private static void PrintProject(UIDocument uiDoc, UserImageValues userValues, string imagesFolder, bool is3D)
		{
			string initialName = PrintHelper.GetFileName(uiDoc.Document);
			string filePath = Path.Combine(imagesFolder, initialName);
			if (is3D)
			{
				PrintHelper.SetActive3DView(uiDoc);
				PrintHelper.View3DChangesCommit(uiDoc, userValues);
			}
			else
			{
				PrintHelper.SetActive2DView(uiDoc);
				PrintHelper.View2DChangesCommit(uiDoc, userValues);
			}
			PrintHelper.PrintImageTransaction(uiDoc, userValues, filePath, true);
		}

		public static void CreateProjectFromFamilySymbol(UIDocument uiDoc, FamilySymbol symbol, string pathProject)
		{
			string umlautName = new FileInfo(pathProject).Name;
			string normalizedName = PrintHelper.CorrectFileName(umlautName);
			pathProject = pathProject.Replace(umlautName, normalizedName);
			if (File.Exists(pathProject) && PrintHelper.IsFileAccessible(pathProject))
				File.Delete(pathProject);
			Debug.WriteLine(pathProject);
			uiDoc.Document.SaveAs(pathProject);
		}

		public static void RemoveExcessFamilies(Document doc)
		{
			FilteredElementCollector famCollector
				= new FilteredElementCollector(doc);
			famCollector.OfClass(typeof(Family));

			var familiesList = famCollector.ToElements();

			for (int i = 0; i < familiesList.Count; i++)
			{
				DeleteElementCommit(doc, familiesList[i]);
			}
		}

		public static Family LoadFamily(UIDocument uiDoc, FileInfo file)
		{
			Document doc = uiDoc.Document;
			Family family = null;
			bool success = false;
			using (Transaction transaction = new Transaction(doc))
			{
				transaction.Start("Load Family");
				success = doc.LoadFamily(file.FullName, out family);
				transaction.Commit();
			}
			if (!success)
				IsFamilyExists(uiDoc, ref family, file);
			return family;
		}

		public static bool IsFamilyExists(UIDocument uiDoc, ref Family family, FileInfo file)
		{
			string familyName = file.Name.Replace(file.Extension, string.Empty);
			var families = new FilteredElementCollector(uiDoc.Document)
				.OfClass(typeof(Family))
				.ToElements();
			foreach (var i in families)
			{
				if (i.Name != familyName) continue;
				family = i as Family;
				return true;
			}
			return false;
		}

		public static void InsertInstanceIntoProject(UIDocument uiDoc, FamilySymbol symbol)
		{
			Document doc = uiDoc.Document;
			View view = null;

			FilteredElementCollector viewCollector = new FilteredElementCollector(doc);
			viewCollector.OfClass(typeof(View));
			foreach (Element viewElement in viewCollector)
			{
				View tmpView = (View)viewElement;
				if (tmpView.Name.Equals($"{App.Translator.GetValue(Translator.Keys.level1Name)}")
				    && tmpView.ViewType == ViewType.EngineeringPlan)
				{
					view = tmpView;
					break;
				}
			}

			if (view == null)
			{
				view = CreateStructuralPlan(doc);
			}

			FamilyInstance createdInstance = null;
			using (var transaction = new Transaction(doc, "Insert Symbol"))
			{
				transaction.Start();
				symbol.Activate();
				XYZ point = new XYZ(0, 0, 0);
				Level level = view.GenLevel;
				Element host = level as Element;
				createdInstance = doc.Create.NewFamilyInstance(point, symbol, host, StructuralType.NonStructural);
				transaction.Commit();
			}

			if (createdInstance.get_BoundingBox(view) == null)
			{
				createdInstance = InsertInstanceIntoWall(uiDoc, view, symbol);
			}


			//var createdInstance = InsertInstanceIntoRailing(uiDoc, view, symbol);

			//var createdInstance = InsertInstanceIntoCurtainWall(uiDoc, view, symbol);
			//CreateElevation(doc, createdInstance, view);

			//View v = new FilteredElementCollector(doc)
			//	.OfClass(typeof(View))
			//	.Cast<View>()
			//	.First(x => x.ViewType == ViewType.Elevation);
			//uiDoc.ActiveView = v;

			//var walls = new FilteredElementCollector(doc, uiDoc.ActiveView.Id).OfClass(typeof(Wall)).Select(x => x.Id).ToList();
			//var levels = new FilteredElementCollector(doc, uiDoc.ActiveView.Id).OfClass(typeof(Level)).Select(x => x.Id).ToList();

			//List<ElementId> tmpList = new List<ElementId>();
			////if (createdInstance is FamilyInstance && (createdInstance as FamilyInstance)?.Host?.Id != null)
			////	tmpList.Add((createdInstance as FamilyInstance).Host.Id);
			////tmpList.AddRange(walls);
			//tmpList.AddRange(levels);
			//if (tmpList.Count > 0)
			//{
			//	try
			//	{
			//		PrintHelper.HideElementsCommit(doc, uiDoc.ActiveView, tmpList);
			//		PrintHelper.HideElementsCommit(uiDoc, tmpList);
			//	}
			//	catch (Exception exc)
			//	{
			//		Debug.WriteLine($"\tCOULD NOT HIDE ELEMENTS\n{exc.Message}\n{exc.StackTrace}");
			//	}
			//}
		}

		/// <summary>
		/// Method draws three ModelLines in project, which intersect in the curren Point. Uses transaction
		/// </summary>
		/// <param name="doc">Document in which lines must be drawn</param>
		/// <param name="point">Point to be highlited with lines</param>
		/// <param name="point">Size of the lines</param>
		/// <param name="isSubtransaction">Does this operation must be executed in transaction or subtransaction modes</param>
		public static void DrawLinesInterconnection(Document doc, XYZ mainPoint, int size = 5, bool isSubtransaction = false)
		{
			if (!isSubtransaction)
			{
				using (Transaction transaction = new Transaction(doc, "Drawing lines"))
				{
					transaction.Start();
					drawLinesInterconnection(doc, mainPoint, size);
					transaction.Commit();
				}
				return;
			}

			using (SubTransaction subtransaction = new SubTransaction(doc))
			{
				subtransaction.Start();
				drawLinesInterconnection(doc, mainPoint, size);
				subtransaction.Commit();
			}
		}

		private static void drawLinesInterconnection(Document doc, XYZ mainPoint, int size)
		{
			XYZ z1 = new XYZ(mainPoint.X, mainPoint.Y, mainPoint.Z - size);
			XYZ z2 = new XYZ(mainPoint.X, mainPoint.Y, mainPoint.Z + size);
			Line zLine = Line.CreateBound(z1, z2);

			Plane zPlane = Plane.CreateByThreePoints(XYZ.Zero, z1, z2);
			SketchPlane zSketch = SketchPlane.Create(doc, zPlane);

			XYZ y1 = new XYZ(mainPoint.X, mainPoint.Y + size, mainPoint.Z);
			XYZ y2 = new XYZ(mainPoint.X, mainPoint.Y - size, mainPoint.Z);
			Line yLine = Line.CreateBound(y1, y2);

			Plane yPlane = Plane.CreateByThreePoints(XYZ.Zero, y1, y2);
			SketchPlane ySketch = SketchPlane.Create(doc, yPlane);

			XYZ x1 = new XYZ(mainPoint.X + size, mainPoint.Y, mainPoint.Z);
			XYZ x2 = new XYZ(mainPoint.X - size, mainPoint.Y, mainPoint.Z);
			Line xLine = Line.CreateBound(x1, x2);

			Plane xPlane = Plane.CreateByThreePoints(XYZ.Zero, x1, x2);
			SketchPlane xSketch = SketchPlane.Create(doc, xPlane);

			doc.Create.NewModelCurve(zLine, zSketch);
			doc.Create.NewModelCurve(yLine, ySketch);
			doc.Create.NewModelCurve(xLine, xSketch);
		}
		private static void SetViewSectionBox(Document doc, ElevationMarker marker, BoundingBoxXYZ box, View view)
		{
			XYZ center = box.Min.Add(box.Max).Multiply(0.5);

			double width = box.Max.X - box.Min.X;
			double height = box.Max.Z - box.Min.Z;
			double depth = box.Max.Y - box.Min.Y;

			using (Transaction t = new Transaction(doc, "set elevation bouding box"))
			{
				t.Start();
				ViewSection elevationView = marker.CreateElevation(doc, view.Id, 0);
				BoundingBoxXYZ bb = elevationView.get_BoundingBox(null);
				bb.Min = new XYZ(box.Min.X, center.Y, box.Min.Z);//new XYZ(center.Y - width / 2, center.Z, 0);
				bb.Max = new XYZ(box.Max.X, center.Y + height, box.Max.Z);//new XYZ(center.Y + width / 2, center.Z + height, 0); 
				elevationView.CropBox = bb;
				//elevationView.CropBoxVisible = false;
				Parameter p = elevationView.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
				p.Set(depth);
				t.Commit();
			}
		}

		private static ElevationMarker CreateElevationMarker(Document doc, ElementId viewFamilyTypeId, XYZ center)
		{
			ElevationMarker marker = null;
			using (Transaction t = new Transaction(doc, "create elevation"))
			{
				t.Start();
				marker = ElevationMarker.CreateElevationMarker(doc, viewFamilyTypeId, center, 30);
				t.Commit();
			}
			return marker;
		}

		private static void CreateElevation(Document doc, Element instance, View view)
		{
			var box = instance.get_BoundingBox(doc.ActiveView);
			XYZ center = box.Min.Add(box.Max).Multiply(0.5);

			double width = box.Max.X - box.Min.X;
			double height = box.Max.Z - box.Min.Z;
			double angle = 0.5 * Math.PI;
			double depth = box.Max.Y - box.Min.Y;

			var vft = new FilteredElementCollector(doc)
				.OfClass(typeof(ViewFamilyType))
				.Cast<ViewFamilyType>()
				.First<ViewFamilyType>(vftype => vftype.ViewFamily == ViewFamily.Elevation);

			using (Transaction t = new Transaction(doc, "elevation"))
			{
				t.Start();
				ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, center, 30);
				ViewSection elevationView = marker.CreateElevation(doc, view.Id, 0);

				View v = new FilteredElementCollector(doc)
					.OfClass(typeof(View))
					.Cast<View>()
					.First(x => x.ViewType == ViewType.Elevation);
				box = instance.get_BoundingBox(v);
				center = box.Min.Add(box.Max).Multiply(0.5);

				BoundingBoxXYZ bb = elevationView.get_BoundingBox(null);
				bb.Min = new XYZ(box.Min.X - 0.2, box.Min.Z - 0.2, box.Min.Y);//new XYZ(center.Y - width / 2, center.Z, 0);
				bb.Max = new XYZ(box.Max.X + 0.2, box.Max.Z + 1, box.Max.Y);//new XYZ(center.Y + width / 2, center.Z + height, 0); center.Z + height * 2
				elevationView.CropBox = bb;
				elevationView.CropBoxVisible = false;
				Parameter p = elevationView.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
				p.Set(depth);

				//Line l = Line.CreateBound(center, center + XYZ.BasisZ);
				//ElementTransformUtils.RotateElement(doc, marker.Id, l, angle);

				t.Commit();
			}
		}


		private static Railing CreateRailing(UIDocument uiDoc, int offset, RailingType gel, View view)
		{
			Railing createdRailing = null;
			Document doc = uiDoc.Document;
			using (Transaction t = new Transaction(doc))
			{
				t.Start("Create Railing");
				
				var initBal = gel.BalusterPlacement.BalusterPattern.GetBaluster(0);
				//initBal.DistanceFromPreviousOrSpace = 300;
				//// print in Revit2021 to use this function
				//initBal.BaseReferenceName = BalusterInfo.GetReferenceNameForHost();
				//BalusterInfo.GetReferenceNameForTopRail();
				initBal.TopReferenceName = gel.BalusterPlacement.PostPattern.StartPost.TopReferenceName;
				initBal.BaseOffset = initBal.TopOffset = initBal.Offset = 0;
				var balAmount = gel.BalusterPlacement.BalusterPattern.GetBalusterCount();
				gel.BalusterPlacement.BalusterPattern.BreakPattern = BreakPatternCondition.Never;
				gel.BalusterPlacement.BalusterPattern.DistributionJustification = PatternJustification.Center;
				//XYZ start = new XYZ(0, offset, 0);
				//XYZ end = new XYZ(10, offset, 0);
				XYZ start = new XYZ(0, 0, 0);
				XYZ end = new XYZ(10, 0, 0);
				Line geomLine = Line.CreateBound(start, end);
				List<Curve> list = new List<Curve>()
					{
						geomLine
					};
				CurveLoop loop = CurveLoop.Create(list);
				createdRailing = Railing.Create(doc, loop, gel.Id, view.GenLevel.Id);
				t.Commit();
			}
			return createdRailing;
		}

		private static Railing InsertInstanceIntoRailing(UIDocument uiDoc, View view, FamilySymbol symbol)
		{
			Document doc = uiDoc.Document;
			Railing railing = null;
			var railingType = new FilteredElementCollector(doc)
				.OfClass(typeof(RailingType))
				.Cast<RailingType>()
				.ToList();

			var railings = new FilteredElementCollector(doc)
				.OfClass(typeof(RailingType))
				.Cast<RailingType>()
				.ToList();

			Railing ral = null;
			RailingType type = null;
			type.BalusterPlacement.PostPattern.CornerPost.BalusterFamilyId = symbol.Id;
			type.BalusterPlacement.PostPattern.StartPost.BalusterFamilyId = symbol.Id;
			type.BalusterPlacement.PostPattern.EndPost.BalusterFamilyId = symbol.Id;
			type.BalusterPlacement.PostPattern.CornerPostCondition = BreakCornerCondition.EachSegmentEnd;
			type.BalusterPlacement.PostPattern.CornerPostAngle = 0;



			//BreakPattern = BreakPatternCondition.EachSegmentEnd,
			//DistributionJustification = PatternJustification.Center

			//.FirstOrDefault(x => x.Name.Equals(symbol.Name));
			//using (Transaction t = new Transaction(doc))
			//{
			//	t.Start("Create Railing");

			//	XYZ start = new XYZ(0, 0, 0);
			//	XYZ end = new XYZ(10, 0, 0);
			//	Line geomLine = Line.CreateBound(start, end);
			//	List<Curve> list = new List<Curve>()
			//	{
			//		geomLine
			//	};

			//	CurveLoop loop = CurveLoop.Create(list);
			//	railing = Railing.Create(doc, loop, railingType.Id, view.GenLevel.Id);

			//	t.Commit();
			//}
			return railing;
		}

		private static Element InsertInstanceIntoCurtainWall(UIDocument uiDoc, View view, FamilySymbol symbol)
		{
			Document doc = uiDoc.Document;
			Wall wall = null;
			WallType wallType = new FilteredElementCollector(doc)
				.OfClass(typeof(WallType))
				.Cast<WallType>()
				.FirstOrDefault(x => x.Kind == WallKind.Curtain);
			using (Transaction t = new Transaction(doc))
			{
				t.Start("Create Wall");
				XYZ start = new XYZ(0, 0, 0);
				XYZ end = new XYZ(10, 0, 0);
				Line geomLine = Line.CreateBound(start, end);
				wall = Wall.Create(doc, geomLine , wallType.Id, view.GenLevel.Id, 10, 0, false, false);
				wall.WallType = wallType;
				wall.WallType.get_Parameter(BuiltInParameter.AUTO_PANEL_WALL).Set(symbol.Id);
				t.Commit();
			}
			return wall;
		}

		private static FamilyInstance InsertInstanceIntoWall(UIDocument uiDoc, View view, FamilySymbol symbol)
		{
			FamilyInstance instance = null;
			Document doc = uiDoc.Document;
			Wall wall = null;
			using (Transaction t = new Transaction(doc))
			{
				t.Start("Create Wall");
				XYZ start = new XYZ(0, 0, 0);
				XYZ end = new XYZ(10, 0, 0);
				Line geomLine = Line.CreateBound(start, end);
				wall = Wall.Create(doc, geomLine, view.GenLevel.Id, true);
				t.Commit();
			}
			using (var transaction = new Transaction(doc, "Insert Symbol"))
			{
				transaction.Start();
				symbol.Activate();
				XYZ point = new XYZ(0, 0, 0);
				Level level = view.GenLevel;
				Element host = wall as Element;
				instance = doc.Create.NewFamilyInstance(point, symbol, host, StructuralType.NonStructural);
				transaction.Commit();
			}
			List<ElementId> tmpList = new List<ElementId>();
			tmpList.Add(wall.Id);
			PrintHelper.HideElementsCommit(uiDoc, tmpList);
			return instance;
		}

		public static View CreateStructuralPlan(Document doc)
		{
			FilteredElementCollector vftCollector = new FilteredElementCollector(doc);
			vftCollector.OfClass(typeof(ViewFamilyType));
			ViewFamilyType viewFamType = vftCollector
				.Cast<ViewFamilyType>()
				.FirstOrDefault(vftype => vftype.ViewFamily.Equals(ViewFamily.StructuralPlan));

			FilteredElementCollector lvlCollector = new FilteredElementCollector(doc);
			lvlCollector.OfClass(typeof(Level));
			Level level1 = lvlCollector
				.Cast<Level>()
				.FirstOrDefault(lvl => lvl.Name.Equals($"{App.Translator.GetValue(Translator.Keys.level1Name)}"));

			if (level1 == null)
				level1 = lvlCollector.Cast<Level>().FirstOrDefault();

			ViewPlan vp = null;
			using (Transaction transaction = new Transaction(doc, "Create Plan"))
			{
				transaction.Start();
				vp = ViewPlan.Create(doc, viewFamType.Id, level1.Id);
				transaction.Commit();
			}

			return vp;
		}

		public static void RemoveExistingInstances(Document doc, ElementId id)
		{
			var familyInstances = new FilteredElementCollector(doc)
				.OfClass(typeof(FamilyInstance))
				.ToElements();
			foreach (var i in familyInstances)
			{
				if (i.Id != id)
				{
					DeleteElementCommit(doc, i);
				}
			}
			var wallInstances = new FilteredElementCollector(doc)
				.OfClass(typeof(Wall))
				.ToElements();
			foreach (var i in wallInstances)
			{
				if (i.Id != id)
				{
					DeleteElementCommit(doc, i);
				}
			}
		}

		public static void DeleteElementCommit(Document doc, Element element)
		{
			using (Transaction transaction = new Transaction(doc))
			{
				try
				{
					transaction.Start("Delete");
					doc.Delete(element.Id);
					transaction.Commit();
				}
				catch(Exception exc)
				{
					transaction.RollBack();
					App.Logger.WriteLine($"Error during deleting element -> {element.Name}\n{exc.Message}");
					Debug.WriteLine($"Error during deleting element -> {element.Name}\n{exc.Message}");
				}
			}
		}

		public static List<FileInfo> GetFamilyFilesFromFolder(DirectoryInfo familiesFolder)
		{
			if (familiesFolder == null) return null;

			var familyFilesList = familiesFolder.GetFiles("*.rfa", SearchOption.AllDirectories).ToList(); // .Where(x => x.Extension.Equals(".rfa"))
			if (!familyFilesList.Any())
			{
				new TaskDialog("Fail")
				{
					TitleAutoPrefix = false,
					MainIcon = TaskDialogIcon.TaskDialogIconWarning,
					MainContent = ".rfa files have not been found in specified folder."
				}.Show();
				return null;
			}
			return familyFilesList;
		}
	}
}
