// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers;

namespace SharpEmu.Libs.Gpu.Buffers;

public sealed class WriteTickMap
{
    private BlockedEntryStore? _storage;

    private readonly record struct Entry(ulong Begin, ulong End, ulong Tick);

    public int Count => _storage?.Count ?? 0;

    public ulong Floor => _storage?.Floor ?? 0;

    public void Assign(ulong begin, ulong size, ulong tick)
    {
        if (size == 0) return;
        (_storage ??= new()).Assign(begin, size, tick);
    }

    public ulong MaxTick(ulong begin, ulong size) => _storage?.MaxTick(begin, size) ?? 0;

    public void Prune(ulong below) => _storage?.Prune(below);

    // Small sorted blocks bound interval-array movement to the edited edges of the map.
    private sealed class BlockedEntryStore
    {
        private const int BlockCapacity = 128;
        private const int InitialBlockCapacity = 16;
        // Blocks a merge, prune or splice drops are kept for the next split or new block: a map
        // that is pruned and refilled every few frames would otherwise allocate and regrow all of
        // its blocks' arrays each time. Spares only ever come from this map's own blocks, so they
        // never hold more than the map did at its largest.
        private readonly List<Block> _blocks = [];
        private Stack<Block>? _spareBlocks;
        private int _count;
        private ulong _floor;

        private readonly record struct Position(int Block, int Index);

        private sealed class Block
        {
            internal Entry[] Entries = new Entry[InitialBlockCapacity + 3];
            internal int Count;

            internal void EnsureCapacity(int required)
            {
                if (required <= Entries.Length) return;
                var capacity = Math.Min(BlockCapacity + 3, Math.Max(required, Entries.Length * 2));
                Array.Resize(ref Entries, capacity);
            }

            internal void Write(ReadOnlySpan<Entry> entries)
            {
                EnsureCapacity(entries.Length);
                entries.CopyTo(Entries);
                Count = entries.Length;
            }
        }

        internal int Count => _count;
        internal ulong Floor => _floor;

        internal void Assign(ulong begin, ulong size, ulong tick)
        {
            if (size == 0) return;

            var end = begin + size;
            var start = FirstEndingAfter(begin);
            if (TryGet(start, out var current))
            {
                if (current.Begin <= begin && current.End >= end && current.Tick == tick)
                    return;
            }

            var after = start;
            var removed = 0;
            while (TryGet(after, out var entry) && entry.Begin < end)
            {
                removed++;
                after = Next(after);
            }

            Entry? left = null;
            Entry? right = null;
            if (removed != 0)
            {
                var first = Get(start);
                if (first.Begin < begin) left = first with { End = begin };
                var last = Get(Previous(after));
                if (last.End > end) right = last with { Begin = end };
            }

            var middle = new Entry(begin, end, tick);
            if (left is { } leftPiece && leftPiece.Tick == tick)
            {
                middle = middle with { Begin = leftPiece.Begin };
                left = null;
            }

            if (right is { } rightPiece && rightPiece.Tick == tick)
            {
                middle = middle with { End = rightPiece.End };
                right = null;
            }

            var spliceStart = start;
            var spliceRemoved = removed;
            if (left is null && TryPrevious(spliceStart, out var previousPosition, out var previous) &&
                previous.End == middle.Begin && previous.Tick == tick)
            {
                middle = middle with { Begin = previous.Begin };
                spliceStart = previousPosition;
                spliceRemoved++;
            }

            if (right is null && TryGet(after, out var next) && next.Begin == middle.End && next.Tick == tick)
            {
                middle = middle with { End = next.End };
                spliceRemoved++;
            }

            Span<Entry> replacements = stackalloc Entry[3];
            var replacementCount = 0;
            if (left is { } before) replacements[replacementCount++] = before;
            replacements[replacementCount++] = middle;
            if (right is { } afterPiece) replacements[replacementCount++] = afterPiece;
            Splice(spliceStart, spliceRemoved, replacements[..replacementCount]);
        }

        internal ulong MaxTick(ulong begin, ulong size)
        {
            var tick = _floor;
            if (size == 0) return tick;

            var end = begin + size;
            for (var position = FirstEndingAfter(begin); TryGet(position, out var entry) && entry.Begin < end; position = Next(position))
                tick = Math.Max(tick, entry.Tick);
            return tick;
        }

        internal void Prune(ulong below)
        {
            var writeBlock = 0;
            var keptEntries = 0;
            for (var readBlock = 0; readBlock < _blocks.Count; readBlock++)
            {
                var block = _blocks[readBlock];
                var writeEntry = 0;
                for (var readEntry = 0; readEntry < block.Count; readEntry++)
                {
                    var entry = block.Entries[readEntry];
                    if (entry.Tick < below)
                    {
                        _floor = Math.Max(_floor, entry.Tick);
                    }
                    else
                    {
                        block.Entries[writeEntry++] = entry;
                    }
                }

                if (writeEntry == 0)
                {
                    ReturnBlock(block);
                    continue;
                }

                block.Count = writeEntry;
                _blocks[writeBlock++] = block;
                keptEntries += writeEntry;
            }

            _blocks.RemoveRange(writeBlock, _blocks.Count - writeBlock);
            _count = keptEntries;
            for (var index = 0; index + 1 < _blocks.Count;)
            {
                if (!TryMergeBlocks(index, index + 1)) index++;
            }
        }

        private Position FirstEndingAfter(ulong address)
        {
            var low = 0;
            var high = _blocks.Count;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                var block = _blocks[middle];
                if (block.Entries[block.Count - 1].End <= address) low = middle + 1;
                else high = middle;
            }

            if (low == _blocks.Count) return new Position(low, 0);
            var entries = _blocks[low];
            var itemLow = 0;
            var itemHigh = entries.Count;
            while (itemLow < itemHigh)
            {
                var middle = itemLow + ((itemHigh - itemLow) >> 1);
                if (entries.Entries[middle].End <= address) itemLow = middle + 1;
                else itemHigh = middle;
            }

            return new Position(low, itemLow);
        }

        private bool TryGet(Position position, out Entry entry)
        {
            if ((uint)position.Block < (uint)_blocks.Count && (uint)position.Index < (uint)_blocks[position.Block].Count)
            {
                entry = _blocks[position.Block].Entries[position.Index];
                return true;
            }

            entry = default;
            return false;
        }

        private Entry Get(Position position) => _blocks[position.Block].Entries[position.Index];

        private Position Next(Position position)
        {
            var block = _blocks[position.Block];
            var nextIndex = position.Index + 1;
            return nextIndex < block.Count ? new Position(position.Block, nextIndex) : new Position(position.Block + 1, 0);
        }

        private bool TryPrevious(Position position, out Position previousPosition, out Entry previous)
        {
            if (position.Block < _blocks.Count && position.Index > 0)
            {
                previousPosition = new Position(position.Block, position.Index - 1);
                previous = Get(previousPosition);
                return true;
            }

            var previousBlock = Math.Min(position.Block, _blocks.Count) - 1;
            if (previousBlock >= 0)
            {
                previousPosition = new Position(previousBlock, _blocks[previousBlock].Count - 1);
                previous = Get(previousPosition);
                return true;
            }

            previousPosition = default;
            previous = default;
            return false;
        }

        private Position Previous(Position position) =>
            TryPrevious(position, out var previousPosition, out _) ? previousPosition : throw new InvalidOperationException("Missing previous interval.");

        private void Splice(Position start, int removed, ReadOnlySpan<Entry> replacements)
        {
            if (_blocks.Count == 0)
            {
                if (replacements.IsEmpty) return;
                var first = RentBlock();
                first.Write(replacements);
                _blocks.Add(first);
                _count = replacements.Length;
                return;
            }

            if (start.Block == _blocks.Count)
                start = new Position(_blocks.Count - 1, _blocks[^1].Count);

            var endBlock = start.Block;
            var endIndex = start.Index;
            var remaining = removed;
            while (remaining > 0)
            {
                if (endBlock >= _blocks.Count) throw new InvalidOperationException("Write-tick splice exceeds the interval map.");
                var available = _blocks[endBlock].Count - endIndex;
                if (remaining < available)
                {
                    endIndex += remaining;
                    remaining = 0;
                }
                else
                {
                    remaining -= available;
                    endBlock++;
                    endIndex = 0;
                }
            }

            if (endBlock == start.Block)
            {
                SpliceWithinBlock(start.Block, start.Index, removed, replacements);
                return;
            }

            SpliceAcrossBlocks(start.Block, start.Index, endBlock, endIndex, removed, replacements);
        }

        private void SpliceWithinBlock(int blockIndex, int index, int removed, ReadOnlySpan<Entry> replacements)
        {
            var block = _blocks[blockIndex];
            var newCount = block.Count - removed + replacements.Length;
            block.EnsureCapacity(newCount);
            var tailStart = index + removed;
            var tailCount = block.Count - tailStart;
            block.Entries.AsSpan(tailStart, tailCount).CopyTo(block.Entries.AsSpan(index + replacements.Length, tailCount));
            replacements.CopyTo(block.Entries.AsSpan(index));
            block.Count = newCount;
            _count = _count - removed + replacements.Length;

            if (newCount == 0)
            {
                _blocks.RemoveAt(blockIndex);
                ReturnBlock(block);
                CoalesceAt(blockIndex);
                return;
            }

            if (newCount > BlockCapacity) SplitBlock(blockIndex, block);
            else CoalesceAt(blockIndex);
        }

        // Rebuild only the two boundary blocks; fully covered interior blocks are dropped.
        private void SpliceAcrossBlocks(int firstBlockIndex, int firstIndex, int endBlockIndex, int endIndex,
            int removed, ReadOnlySpan<Entry> replacements)
        {
            var lastBlockIndex = endBlockIndex < _blocks.Count ? endBlockIndex : _blocks.Count - 1;
            var prefixCount = firstIndex;
            var suffixCount = endBlockIndex < _blocks.Count ? _blocks[endBlockIndex].Count - endIndex : 0;
            var newCount = prefixCount + replacements.Length + suffixCount;
            var removedBlockCount = lastBlockIndex - firstBlockIndex + 1;
            var blockCount = (newCount + BlockCapacity - 1) / BlockCapacity;
            var block0 = newCount == 0 ? null : _blocks[firstBlockIndex];
            var block1 = blockCount > 1
                ? (removedBlockCount > 1 ? _blocks[firstBlockIndex + 1] : RentBlock())
                : null;
            var block2 = blockCount > 2
                ? (removedBlockCount > 2 ? _blocks[firstBlockIndex + 2] : RentBlock())
                : null;
            var scratch = ArrayPool<Entry>.Shared.Rent(Math.Max(1, newCount));
            try
            {
                var written = 0;
                _blocks[firstBlockIndex].Entries.AsSpan(0, prefixCount).CopyTo(scratch.AsSpan(written));
                written += prefixCount;
                replacements.CopyTo(scratch.AsSpan(written));
                written += replacements.Length;
                if (suffixCount != 0)
                {
                    _blocks[endBlockIndex].Entries.AsSpan(endIndex, suffixCount).CopyTo(scratch.AsSpan(written));
                    written += suffixCount;
                }

                if (written != newCount) throw new InvalidOperationException("Write-tick splice produced an invalid interval count.");
                if (newCount != 0)
                {
                    WriteChunk(block0!, scratch.AsSpan(0, Math.Min(BlockCapacity, newCount)));
                    if (blockCount > 1) WriteChunk(block1!, scratch.AsSpan(BlockCapacity, Math.Min(BlockCapacity, newCount - BlockCapacity)));
                    if (blockCount > 2) WriteChunk(block2!, scratch.AsSpan(BlockCapacity * 2, newCount - BlockCapacity * 2));
                }
            }
            finally
            {
                ArrayPool<Entry>.Shared.Return(scratch);
            }

            _count = _count - removed + replacements.Length;
            if (blockCount == 0)
            {
                ReturnBlocks(firstBlockIndex, removedBlockCount);
                _blocks.RemoveRange(firstBlockIndex, removedBlockCount);
                CoalesceAt(firstBlockIndex);
                return;
            }

            if (removedBlockCount > blockCount)
            {
                // The blocks past the rewritten ones held only replaced entries.
                ReturnBlocks(firstBlockIndex + blockCount, removedBlockCount - blockCount);
                _blocks.RemoveRange(firstBlockIndex + blockCount, removedBlockCount - blockCount);
            }
            else if (blockCount > removedBlockCount)
            {
                for (var index = removedBlockCount; index < blockCount; index++)
                    _blocks.Insert(firstBlockIndex + index, index == 1 ? block1! : block2!);
            }

            CoalesceAt(firstBlockIndex);
        }

        private static void WriteChunk(Block block, ReadOnlySpan<Entry> entries) => block.Write(entries);

        private void SplitBlock(int blockIndex, Block block)
        {
            var right = RentBlock();
            var split = block.Count / 2;
            right.Write(block.Entries.AsSpan(split, block.Count - split));
            block.Count = split;
            _blocks.Insert(blockIndex + 1, right);
        }

        private void CoalesceAt(int blockIndex)
        {
            if (_blocks.Count < 2) return;
            var index = Math.Clamp(blockIndex, 0, _blocks.Count - 1);
            if (index > 0 && TryMergeBlocks(index - 1, index)) index--;
            if (index + 1 < _blocks.Count) _ = TryMergeBlocks(index, index + 1);
        }

        private bool TryMergeBlocks(int leftIndex, int rightIndex)
        {
            var left = _blocks[leftIndex];
            var right = _blocks[rightIndex];
            if (left.Count + right.Count > BlockCapacity) return false;
            left.EnsureCapacity(left.Count + right.Count);
            right.Entries.AsSpan(0, right.Count).CopyTo(left.Entries.AsSpan(left.Count));
            left.Count += right.Count;
            _blocks.RemoveAt(rightIndex);
            ReturnBlock(right);
            return true;
        }

        private Block RentBlock()
        {
            if (_spareBlocks is null || !_spareBlocks.TryPop(out var block)) return new Block();
            block.Count = 0;
            return block;
        }

        private void ReturnBlock(Block block) => (_spareBlocks ??= new()).Push(block);

        private void ReturnBlocks(int index, int count)
        {
            for (var offset = 0; offset < count; offset++) ReturnBlock(_blocks[index + offset]);
        }
    }
}
