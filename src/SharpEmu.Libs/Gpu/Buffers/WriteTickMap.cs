// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

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

            _entries.RemoveRange(index, removed);
        }

        var insert = index;
        if (left is { } leftPiece)
        {
            _entries.Insert(insert++, leftPiece);
        }

        _entries.Insert(insert, new Entry(begin, end, tick));
        var middle = insert++;
        if (right is { } rightPiece)
        {
            _entries.Insert(insert, rightPiece);
        }

        MergeAround(middle);
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
