using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Grader
{
	// Simulates ForEach. Used instead of System.Threading.Tasks.Parallel for debugging.
	class Sequential<T>
	{
		public static Action<IEnumerable<T>, Action<T>> ForEach = Sequential<T>.ForEachFunc;

		private static void ForEachFunc<T>(IEnumerable<T> collection, Action<T> action)
		{
			foreach (var item in collection)
			{
				action(item);
			}
		}
	}
}
