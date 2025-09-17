/***********************************************************************************************************************
*  Copyright (c) 2025,  Skyline Communications NV  All Rights Reserved.												   *
************************************************************************************************************************

By using this script, you expressly agree with the usage terms and conditions set out below.
This script and all related materials are protected by copyrights and other intellectual property rights that
exclusively belong to Skyline Communications.

A user license granted for this script is strictly for personal use only. This script may not be used in any way by
anyone without the prior written consent of Skyline Communications. Any sublicensing of this script is forbidden.

Any modifications to this script by the user are only allowed for personal use and within the intended purpose of the
script, and will remain the sole responsibility of the user.
Skyline Communications will not be responsible for any damages or malfunctions whatsoever of the script resulting from
a modification or adaptation by the user.

The content of this script is confidential information. The user hereby agrees to keep this confidential information
strictly secret and confidential and not to disclose or reveal it, in whole or in part, directly or indirectly to any
person, entity, organization or administration without the prior written consent of Skyline Communications.

Any inquiries can be addressed to:

	Skyline Communications NV
	Ambachtenstraat 33
	B-8870 Izegem
	Belgium
	Tel.	: +32 51 31 35 69
	Fax.	: +32 51 31 01 29
	E-mail	: info@skyline.be
	Web		: www.skyline.be
	Contact	: Ben Vandenberghe

************************************************************************************************************************
Revision History:

DATE		VERSION		AUTHOR			COMMENTS

16/09/2025	1.0.0.1		GMV, Skyline	Initial version
***********************************************************************************************************************/
namespace SLCASCreateViews
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Threading;
	using Skyline.DataMiner.Automation;
	using Skyline.DataMiner.Core.DataMinerSystem.Automation;
	using Skyline.DataMiner.Core.DataMinerSystem.Common;
	using Skyline.DataMiner.Utils.SecureCoding.SecureIO;

	/// <summary>
	/// Represents a DataMiner Automation script.
	/// </summary>
	public class Script
	{
		#region Fields
		private IDms dms;
		private IEngine engine;
		private IDmsView rootView;
		private Dictionary<string, IDmsView> views;
		#endregion

		#region Public methods

		/// <summary>
		/// The script entry point.
		/// </summary>
		/// <param name="engine">Link with SLAutomation process.</param>
		public void Run(IEngine engine)
		{
			try
			{
				RunSafe(engine);
			}
			catch (ScriptAbortException)
			{
				// Catch normal abort exceptions (engine.ExitFail or engine.ExitSuccess)
				throw; // Comment if it should be treated as a normal exit of the script.
			}
			catch (ScriptForceAbortException)
			{
				// Catch forced abort exceptions, caused via external maintenance messages.
				throw;
			}
			catch (ScriptTimeoutException)
			{
				// Catch timeout exceptions for when a script has been running for too long.
				throw;
			}
			catch (InteractiveUserDetachedException)
			{
				// Catch a user detaching from the interactive script by closing the window.
				// Only applicable for interactive scripts, can be removed for non-interactive scripts.
				throw;
			}
			catch (Exception e)
			{
				engine.ExitFail("Run|Something went wrong: " + e);
			}
		}
		#endregion

		#region Private methods

		/// <summary>
		/// Retry until success or until timeout.
		/// </summary>
		/// <param name="func">Operation to retry.</param>
		/// <param name="timeout">Max TimeSpan during which the operation specified in <paramref name="func"/> can be retried.</param>
		/// <returns>
		/// <c>true</c> if one of the retries succeeded within the specified <paramref name="timeout"/>. Otherwise
		/// <c>false</c>.
		/// </returns>
		private static bool Retry(Func<bool> func, TimeSpan timeout)
		{
			Stopwatch sw = new Stopwatch();
			sw.Start();

			bool success;
			do
			{
				success = func();
				if (!success)
				{
					Thread.Sleep(100);
				}
			}
			while (!success && sw.Elapsed <= timeout);

			return success;
		}

		private void CreateViewInfo(ViewInfo viewInfo, IDmsView parentView)
		{
			// Create first this view
			engine.GenerateInformation($"DEBUG:Creating view {viewInfo.Name} with parent {(parentView != null ? parentView.Name : "NULL")}");
			var viewId = GetOrCreateView(viewInfo.Name, parentView);

			// Now, let's descend on children
			foreach (var child in viewInfo.Children)
			{
				CreateViewInfo(child, viewId);
			}
		}

		private IDmsView GetOrCreateView(string name, IDmsView parent)
		{
			if (views.TryGetValue(name, out IDmsView cachedview))
			{
				return cachedview;
			}

			if (dms.ViewExists(name))
			{
				var view = dms.GetView(name);
				views.Add(name, view);
				return view;
			}

			var parentview = parent == null ? rootView : parent;
			var viewId = dms.CreateView(new ViewConfiguration(name, parentview));
			if (!Retry(() => dms.ViewExists(viewId), TimeSpan.FromSeconds(60)))
			{
				throw new TimeoutException($"Verifying if view \"{name}\" was created timed out (60s).");
			}

			var newview = dms.GetView(viewId);
			views.Add(name, newview);
			return newview;
		}

		private void RunSafe(IEngine engine)
		{
			views = new Dictionary<string, IDmsView>();
			this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
			dms = engine.GetDms();
			engine.GenerateInformation($"DEBUG:Getting root view");
			rootView = dms.GetView(-1);
			string filePath = engine.GetScriptParam("Info").Value;
			var directory = Path.GetDirectoryName(filePath);
			if (string.IsNullOrEmpty(directory))
			{
				filePath = SecurePath.ConstructSecurePath(@"c:\Skyline DataMiner\Documents\DMA_COMMON_DOCUMENTS", filePath);
			}

			if (!File.Exists(filePath))
				throw new FileNotFoundException(filePath);
			engine.GenerateInformation($"DEBUG:Reading import file");

			// Read the file and build all the ViewInfo objects
			List<ViewInfo> list = File.ReadAllLines(filePath)
				.Skip(1)
				.Where(l => !string.IsNullOrEmpty(l))
				.Select(l => ViewInfo.FromLine(l))
				.Where(i => i != null)
				.Distinct()
				.ToList();

			foreach (ViewInfo info in list)
			{
				if (info.ParentViewId == null)
					continue;

				// Let's find the parent of this view
				var parent = list.FirstOrDefault(p => p.Id == info.ParentViewId);
				if (parent != null)
				{
					parent.Children.Add(info);
				}
			}

			engine.GenerateInformation($"DEBUG:We have {list.Count} items to configure");

			list.Where(p => p.ParentViewId == null)
				.ToList()
				.ForEach(
					p =>
					{
						CreateViewInfo(p, null);
					});
		}
		#endregion

	}

	internal class ViewInfo : IEqualityComparer<ViewInfo>
	{
		#region Public properties
		public List<ViewInfo> Children { get; private set; }

		public int Id { get; private set; }

		public string Name { get; private set; }

		public int? ParentViewId { get; private set; }
		#endregion

		#region Public methods
		public static ViewInfo FromLine(string line)
		{
			if (string.IsNullOrEmpty(line))
				return null;
			var parts = line.Split(',').Select(l => l.Trim()).ToArray();
			if (parts.Length != 3)
				return null;
			if (!Int32.TryParse(parts[0], out var id))
				return null;
			int? parent = null;
			if (Int32.TryParse(parts[2], out var parentid) && parentid != id)
				parent = parentid;

			if (string.IsNullOrEmpty(parts[1]) || !ViewInfo.IsValidWindowsDirectoryName(parts[1]))
				return null;

			return new ViewInfo { Id = id, Name = parts[1], ParentViewId = parent, Children = new List<ViewInfo>() };
		}

		public bool Equals(ViewInfo x, ViewInfo y)
		{
			if (x == null || y == null)
				return false;
			if (ReferenceEquals(x, y))
				return true;

			return x.Id == y.Id;
		}

		public int GetHashCode(ViewInfo obj)
		{
			return obj.GetHashCode();
		}

		public override string ToString()
		{
			return $"ViewInfo({Id}, {Name}, {ParentViewId}, {Children.Count})";
		}
		#endregion

		#region Private methods
		private static bool IsValidWindowsDirectoryName(string name)
		{
			if (String.IsNullOrWhiteSpace(name))
				return false;

			// Check for invalid characters
			char[] invalidChars = Path.GetInvalidFileNameChars();
			if (name.IndexOfAny(invalidChars) >= 0)
				return false;

			// Check for reserved names (case-insensitive)
			string[] reservedNames =
			{
				"CON",
				"PRN",
				"AUX",
				"NUL",
				"COM1",
				"COM2",
				"COM3",
				"COM4",
				"COM5",
				"COM6",
				"COM7",
				"COM8",
				"COM9",
				"LPT1",
				"LPT2",
				"LPT3",
				"LPT4",
				"LPT5",
				"LPT6",
				"LPT7",
				"LPT8",
				"LPT9",
			};
			if (reservedNames.Contains(name.ToUpperInvariant()))
				return false;

			// Cannot end with space or period
			if (name.EndsWith(" ") || name.EndsWith("."))
				return false;

			return true;
		}
		#endregion

	}
}
