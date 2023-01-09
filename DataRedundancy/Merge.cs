using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if !DEBUG
using System.Threading.Tasks;
#endif
namespace DataRedundancy
{
	static class Merge
	{
		private static Dictionary<ulong, string> hd;

#if DEBUG
		private static string HashsToTxt(ulong[] hash)
		{
			var lines = new List<string>();
			foreach (var hashLine in hash)
			{
				lines.Add(hd[hashLine]);
			}
			return string.Join("", lines);
		}
#endif

		public static void ExecuteMerge(FileInfo from, FileInfo to, Action<string> alert, FileInfo visualStudioBackupFile = null, FileInfo outputFile = null)
		{
			var local = visualStudioBackupFile ?? to;
			var listFrom = LoadTextFiles(from);
			var listTo = LoadTextFiles(local);
			var positionTo = 0;
			var positionFrom = 0;

#if DEBUG
			hd = new Dictionary<ulong, string>();
			foreach (var list in new List<Line>[] { listFrom, listTo })
			{
				foreach (var item in list)
					if (!hd.ContainsKey(item.Hash))
						hd.Add(item.Hash, item.Text);
			}
#endif

			if (listFrom.Count > 0)
			{
				var hashFirstLine = listFrom[0].Hash;
				var align = listTo.FindIndex(x => x.Hash == hashFirstLine);
				if (align != -1)
					positionTo = align;
				else if (listTo.Count > 0)
				{
					hashFirstLine = listTo[0].Hash;
					var alignFrom = listFrom.FindIndex(x => x.Hash == hashFirstLine);
					if (alignFrom != -1)
						positionFrom = alignFrom;
				}
			}
			var fromHash = listFrom.Select(x => x.Hash).ToArray();
			var toHash = listTo.Select(x => x.Hash).ToArray();
			var fromMap = listFrom.Select(x => new HashCheck() { Hash = x.Hash }).ToArray();
			var toMap = listTo.Select(x => new HashCheck() { Hash = x.Hash }).ToArray();

			List<Block> blocksFrom = null;
			List<Block> blocksTo = null;
#if DEBUG
			blocksFrom = ProcessAllCombination(toMap, fromMap, toHash, fromHash);
			blocksTo = ProcessAllCombination(fromMap, toMap, fromHash, toHash);
			//var txt = HashsToTxt(blocksTo[blocksTo.Count - 1].GetBlockArray());
#else
			var task1 = new Task(() => blocksFrom = ProcessAllCombination(toMap, fromMap, toHash, fromHash));
			var task2 = new Task(() => blocksTo = ProcessAllCombination(fromMap, toMap, fromHash, toHash));
			task1.Start();
			task2.Start();
			Task.WhenAll(task1, task2);
#endif


#if DEBUG
			int lenF = 0;
			foreach (var item in blocksFrom)
			{
				lenF += item.Length;
			}
			if (lenF != fromMap.Length)
				System.Diagnostics.Debugger.Break();
			int lenT = 0;
			foreach (var item in blocksTo)
			{
				lenT += item.Length;
			}
			if (lenT != toMap.Length)
				System.Diagnostics.Debugger.Break();
			var lenFCommon = 0;
			string txF = "";
			//string newF = "";
			foreach (var item in blocksFrom)
			{
				if (!item.IsNew)
				{
					lenFCommon += item.Length;
					//txF += HashsToTxt(item.GetBlockArray());
				}
				else
				{
					//newF += HashsToTxt(item.GetBlockArray());
				}
			}
			var lenTCommon = 0;
			string txT = "";
			//string newT = "";
			foreach (var item in blocksTo)
			{
				if (!item.IsNew)
				{
					lenTCommon += item.Length;
					//txT += HashsToTxt(item.GetBlockArray());
				}
				else
				{
					//newT += HashsToTxt(item.GetBlockArray());
				}
			}
			if (lenTCommon != lenFCommon)
				System.Diagnostics.Debugger.Break();
			if (txF != txT)
				System.Diagnostics.Debugger.Break();

#endif

			// Join the blocks of the two files by eliminating the parts in common
			var blocks = MergeBlock(blocksFrom, blocksTo);

			// Create a dictionary to quickly find the line based on its hash
			var hashLineTable = new Dictionary<ulong, string>();
			foreach (var list in new List<Line>[] { listFrom, listTo })
			{
				foreach (var item in list)
					if (!hashLineTable.ContainsKey(item.Hash))
						hashLineTable.Add(item.Hash, item.Text);
			}

			//Rebuild the output by putting all the blocks together
			var lines = new List<string>();
			foreach (var block in blocks)
			{
				foreach (var hashLine in block.GetBlockArray())
				{
					lines.Add(hashLineTable[hashLine]);
				}
			}

			// Write the merge result to file
			if (outputFile == null)
				outputFile = to;
			try
			{
				File.WriteAllLines(outputFile.FullName, lines);
			}
			catch (Exception e)
			{
				// If the attempt fails it will be updated to the next round!
				if (Support.IsDiskFull(e))
					alert?.Invoke(e.Message);
			}
		}

        private enum Turn
		{
			From,
			To,
		}

		static private List<Block> MergeBlock(List<Block> blocksFrom, List<Block> blocksTo)
		{
			var positionsOfNewBlocks = GetPositionOfNewBlock(blocksFrom);

			var l1 = 0;
			foreach (var item in blocksTo)
			{
				if (!item.IsNew)
					l1 += item.Length;
			}

			SetPositionNoNew(ref blocksTo);
			var result = new List<Block>(blocksTo);

			var l2 = 0;
			foreach (var item in result)
			{
				if (!item.IsNew)
					l2 += item.Length;
			}

			foreach (var newBlock in positionsOfNewBlocks)
			{
				InsertNewBlock(ref result, newBlock);
			}
			return result;
		}

		static private void InsertNewBlock(ref List<Block> blocks, Block newBlock)
		{
#if DEBUG
			var l1 = 0;
			var l = 0;
			foreach (var item in blocks)
			{
				if (!item.IsNew)
					l1 += item.Length;
			}
			//var tx = HashsToTxt(newBlock.GetBlockArray());
#endif
			foreach (var block in blocks.ToArray())
			{
				if (!block.IsNew)
				{
#if DEBUG
					l += block.Length;
					var tx2 = HashsToTxt(block.GetBlockArray());
#endif
					if (block.PositionNoNew == newBlock.PositionNoNew)
					{
						blocks.Insert(blocks.IndexOf(block), newBlock);
						return;
					}
					else if (block.PositionNoNew + block.Length > newBlock.PositionNoNew)
					{
						var length2 = ((int)block.PositionNoNew + block.Length) - (int)newBlock.PositionNoNew;
						var length1 = block.Length - length2;
						SplitBlock(block, length1, out Block block1, out Block block2);
						var index = blocks.IndexOf(block);
						blocks.Remove(block);
						blocks.Insert(index, block2);
						blocks.Insert(index, newBlock);
						blocks.Insert(index, block1);
						return;
					}
					else if (block.PositionNoNew + block.Length == newBlock.PositionNoNew)
					{
						blocks.Insert(blocks.IndexOf(block) + 1, newBlock);
						return;
					}
				}
			}
#if DEBUG
			System.Diagnostics.Debugger.Break(); //Something's wrong
#endif
		}

		static private void SplitBlock(Block input, int split, out Block block1, out Block block2)
		{
			block1 = new Block(input.fullHashOrigin, input.IsNew, input.Position, split);
			block2 = new Block(input.fullHashOrigin, input.IsNew, input.Position + split, input.Length - split);
		}

		private static List<Block> GetPositionOfNewBlock(List<Block> newBlocks)
		{
			var positionsOfNewBlocks = new List<Block>(); // Dictionary block - position;
			var reverseFrom = new List<Block>(newBlocks);
			reverseFrom.Reverse();
			foreach (var block in reverseFrom)
			{
				if (block.IsNew)
				{
#if DEBUG
					//var tx = HashsToTxt(block.GetBlockArray());
#endif

					block.PositionNoNew = 0;
					positionsOfNewBlocks.Insert(0, block);
				}
				else
				{
					foreach (var blockOutput in positionsOfNewBlocks)
					{
						blockOutput.PositionNoNew += block.Length;
					}
				}
			}
			return positionsOfNewBlocks;
		}

		private static void SetPositionNoNew(ref List<Block> blocks)
		{
			for (int i = 0; i < blocks.Count; i++)
			{
				Block block = blocks[i];
				if (block.PositionNoNew == null)
					block.PositionNoNew = 0;
				if (!block.IsNew)
				{
					for (int i2 = i + 1; i2 < blocks.Count; i2++)
					{
						if (blocks[i2].PositionNoNew == null)
							blocks[i2].PositionNoNew = 0;
						blocks[i2].PositionNoNew += block.Length;
					}
				}
			}
		}

		private static List<Block> GetBlocks(HashCheck[] map, ulong[] hash)
		{
			var result = new List<Block>();
			bool? last = null;
			Block newBlock = null;
			for (int i = 0; i < map.Length; i++)
			{
				HashCheck item = map[i];
				if (last != item.Present)
				{
					newBlock = new Block(hash, !item.Present, i);
					result.Add(newBlock);
				}
				newBlock.Length++;
				last = item.Present;
			}
			return result;
		}

		private static List<Block> ProcessAllCombination(HashCheck[] from, HashCheck[] to, ulong[] fromHash, ulong[] toHash)
		{
			var blocks = new List<Block>();
			var max = to.Length > from.Length ? to.Length : from.Length;
			var firstDifferenceFromStart = 0;
			for (int i = 0; i < from.Length && i < to.Length; i++)
			{
				if (from[i].Hash == to[i].Hash)
					firstDifferenceFromStart++;
				else
					break;				
			}
			var L2 = max - firstDifferenceFromStart;
			var maxL = firstDifferenceFromStart > L2 ? firstDifferenceFromStart : L2;

			var firstDifferenceFromEnd = 0;
			for (int i = 0; i < from.Length && i < to.Length; i++)
			{
				if (from[from.Length - 1 - i].Hash == to[to.Length - 1 - i].Hash)
					firstDifferenceFromEnd++;
				else
					break;
			}
			var R2 = max - firstDifferenceFromEnd;
			var maxR = firstDifferenceFromEnd > R2 ? firstDifferenceFromEnd : R2;

			var initialSize = maxL < maxR? maxL : maxR;
			//var initialSize = to.Length < from.Length ? to.Length : from.Length;


			for (int size = initialSize; size > 0; size--)
			{
				int used = 0;
				for (int position = 0; position <= from.Length - size; position++)
				{
					if (position == 0)
					{
						for (int i = position; i < size; i++)
						{
							if (from[i].Used)
								used++;
							else
								used--;
						}
					}
					else
					{
						if (from[position - 1].Used) //update first
							used--;
						else
							used++;
						if (from[position + size - 1].Used) //update last
							used++;
						else
							used--;
					}
					if (used == -size)
					{
						var partial = new ulong[size];
						Array.Copy(fromHash, position, partial, 0, size);
#if DEBUG
						//var find = HashsToTxt(partial);
#endif

						if (Contain(toHash, to, partial, out Block block))
						{
							for (int i = position; i < position + size; i++)
							{
								from[i].Used = true;
							}
							used = size;
#if DEBUG
							//var tx = HashsToTxt(block.GetBlockArray());
#endif
							blocks.Add(block);
						}
					}
				}
			}
			var newBlocks = GetBlocks(to, toHash).FindAll(x => x.IsNew);
			blocks.AddRange(newBlocks);
			blocks.Sort((x, y) => x.Position - y.Position);
			return blocks;
		}
		private static bool Contain(ulong[] arrayHash, HashCheck[] arrayMap, ulong[] subarray, out Block block)
		{
			int hasLinesAlreadyIdentified = 0; //Search only if it finds the sequence between lines of code that do not yet have a match
			for (int i = 0; i < subarray.Length; i++)
			{
				if (arrayMap[i].Present)
					hasLinesAlreadyIdentified++;
				else
					hasLinesAlreadyIdentified--;
			}

			for (int position = 0; position <= arrayHash.Length - subarray.Length; position++)
			{
				if (position > 0)
				{
					if (arrayMap[position - 1].Present) //update first
						hasLinesAlreadyIdentified--;
					else
						hasLinesAlreadyIdentified++;
					if (arrayMap[position + subarray.Length - 1].Present) //update last
						hasLinesAlreadyIdentified++;
					else
						hasLinesAlreadyIdentified--;
				}

				if (hasLinesAlreadyIdentified == -subarray.Length)
				{
					var isEqual = true;
					for (int i = 0; i < subarray.Length; i++)
					{
						if (subarray[i] != arrayHash[position + i])
						{
							isEqual = false;
							break;
						}
					}
					if (isEqual)
					{
						for (int m = position; m < position + subarray.Length; m++)//Mark that the sequence is present in the array
							arrayMap[m].Present = true;
						block = new Block(arrayHash, false, position, subarray.Length);
						return true;
					}
				}
			}
			block = null;
			return false;
		}

		internal static List<Line> LoadTextFiles(FileInfo file)
		{
			var fileLines = File.ReadAllLines(file.FullName);
			var listFile = new List<Line>();
			fileLines.ToList().ForEach(x => listFile.Add(new Line { Text = x, Hash = GetHashCode(x, true) }));
			return listFile;
		}

		internal class Line
		{
			public string Text;
			public ulong Hash;
		}
        public static ulong GetHashCode(string input, bool considerSpaces = false)
        {
            if (!considerSpaces)
            {
                input = input.Replace("\t", "");
                input = input.Replace(" ", "");
            }
            input = input.Replace("\r", "");
            input = input.Replace("\n", "");
            const ulong value = 3074457345618258791ul;
            var hashedValue = value;
            foreach (var t in input)
            {
                hashedValue += t;
                hashedValue *= value;
            }
            return hashedValue;
        }

    }

    struct HashCheck
	{
		public ulong Hash;
		public bool Present;
		public bool Used;
	}
	class Block
	{
		public Block()
		{
			// empty constructor
		}
		public Block(ulong[] hash, bool isNew, int position, int length = 0)
		{
			fullHashOrigin = hash;
			IsNew = isNew;
			Position = position;
			Length = length;
		}
		public readonly ulong[] fullHashOrigin;
		public bool IsNew;
		public int Position;
		public int Length;
		public int? PositionNoNew;
		public ulong[] GetBlockArray()
		{
			return fullHashOrigin.Skip(Position).Take(Length).ToArray();
		}
	}
}
