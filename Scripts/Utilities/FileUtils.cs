using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Unity.AutoLOD.Utilities
{
	public static class FileUtils
	{

		public static void DeleteDirectory(string path)
		{
		    if (Directory.Exists(path) == false)
		        return;

			DirectoryInfo di = new DirectoryInfo(path);

			foreach (FileInfo file in di.GetFiles())
			{
				file.Delete();
			}

			foreach (DirectoryInfo dir in di.GetDirectories())
			{
				DeleteDirectory(dir.FullName);
			}

			di.Delete();
			
		}
	}

}