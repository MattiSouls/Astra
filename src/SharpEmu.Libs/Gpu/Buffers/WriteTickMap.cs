// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;

namespace SharpEmu.Libs.Gpu.Buffers;

public sealed class WriteTickMap
{
    private readonly List<Entry> _entries = [];
    private ulong _floor;

    private readonly record struct Entry(ulong Begin, ulong End, ulong Tick);

    public int Count => _entries.Count;

    public ulong Floor => _floor;

    public void Assign(ulong begin, ulong size, ulong tick)
    {
        if (size == 0)
        {
            return;
        }

        var end = begin + size;
        var index = FirstEndingAfter(begin);
        if (index < _entries.Count)
        {
            var current = _entries[index];
            // Repeated writes within one tick leave this interval unchanged.
            if (current.Begin <= begin && current.End >= end && current.Tick == tick)
            {
                return;
            }

            // Updating an existing interval need not move every later entry.
            if (current.Begin == begin && current.End == end)
            {
                _entries[index] = current with { Tick = tick };
                MergeAround(index);
                return;
            }
        }

        var removed = 0;
        while (index + removed < _entries.Count && _entries[index + removed].Begin < end)
        {
            removed++;
        }

        Entry? left = null;
        Entry? right = null;
        if (removed != 0)
        {
            var first = _entries[index];
            if (first.Begin < begin)
            {
                left = first with { End = begin };
            }

            var last = _entries[index + removed - 1];
            if (last.End > end)
            {
                right = last with { Begin = end };
            }
        }

        ReplaceRange(index, removed, left, new Entry(begin, end, tick), right);
    }

    private void ReplaceRange(int index, int removed, Entry? left, Entry middle, Entry? right)
    {
        // Merge before moving the tail: removing and inserting each piece separately
        // repeatedly shifts every later interval, even when the final count is unchanged.
        if (left is { } leftPiece && leftPiece.Tick == middle.Tick)
        {
            middle = middle with { Begin = leftPiece.Begin };
            left = null;
        }

        if (right is { } rightPiece && rightPiece.Tick == middle.Tick)
        {
            middle = middle with { End = rightPiece.End };
            right = null;
        }

        if (left is null && index > 0)
        {
            var previous = _entries[index - 1];
            if (previous.End == middle.Begin && previous.Tick == middle.Tick)
            {
                middle = middle with { Begin = previous.Begin };
                index--;
                removed++;
            }
        }

        if (right is null && index + removed < _entries.Count)
        {
            var next = _entries[index + removed];
            if (next.Begin == middle.End && next.Tick == middle.Tick)
            {
                middle = middle with { End = next.End };
                removed++;
            }
        }

        var replacements = 1 + (left.HasValue ? 1 : 0) + (right.HasValue ? 1 : 0);
        var oldCount = _entries.Count;
        var newCount = oldCount - removed + replacements;
        if (newCount > oldCount) CollectionsMarshal.SetCount(_entries, newCount);
        var entries = CollectionsMarshal.AsSpan(_entries);
        if (replacements != removed)
        {
            var tail = index + removed;
            entries.Slice(tail, oldCount - tail).CopyTo(entries.Slice(index + replacements));
        }

        if (left is { } before) entries[index++] = before;
        entries[index++] = middle;
        if (right is { } after) entries[index] = after;
        if (newCount < oldCount) CollectionsMarshal.SetCount(_entries, newCount);
    }

    private void MergeAround(int index)
    {
        if (index + 1 < _entries.Count)
        {
            _ = TryMerge(index, index + 1);
        }

        if (index > 0)
        {
            _ = TryMerge(index - 1, index);
        }
    }

    public ulong MaxTick(ulong begin, ulong size)
    {
        var tick = _floor;
        if (size == 0)
        {
            return tick;
        }

        var end = begin + size;
        for (var index = FirstEndingAfter(begin); index < _entries.Count && _entries[index].Begin < end; index++)
        {
            tick = Math.Max(tick, _entries[index].Tick);
        }

        return tick;
    }

    public void Prune(ulong below)
    {
        var kept = 0;
        for (var index = 0; index < _entries.Count; index++)
        {
            var entry = _entries[index];
            if (entry.Tick < below)
            {
                _floor = Math.Max(_floor, entry.Tick);
                continue;
            }

            _entries[kept++] = entry;
        }

        _entries.RemoveRange(kept, _entries.Count - kept);
    }

    private bool TryMerge(int left, int right)
    {
        var a = _entries[left];
        var b = _entries[right];
        if (a.End != b.Begin || a.Tick != b.Tick)
        {
            return false;
        }

        _entries[left] = a with { End = b.End };
        _entries.RemoveAt(right);
        return true;
    }

    private int FirstEndingAfter(ulong address)
    {
        var low = 0;
        var high = _entries.Count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_entries[middle].End <= address)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
